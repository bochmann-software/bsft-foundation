using Bsft.Core;
using Bsft.Idempotency.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Bsft.Idempotency.Replay;

/// <summary>
/// Answers a repeated unsafe request with the response its first attempt produced, instead of
/// running it again.
/// </summary>
/// <remarks>
/// <para>
/// Middleware rather than an endpoint filter, because the response has to be buffered whole before
/// it is stored, and a filter only sees what the handler returns — not what a framework result, a
/// challenge or a redirect writes straight to the response.
/// </para>
/// <para>
/// It must be registered <em>after</em> <c>BsftExceptionMiddleware</c>, so the 428 and the two 409s
/// it raises are mapped into the framework's problem-details shape rather than escaping as a 500.
/// The consequence, stated rather than glossed: a request that fails by <em>throwing</em> is not
/// cached, because the response is produced outside this middleware, so its reservation is released
/// and the same key runs again. Only a status the endpoint wrote itself is stored.
/// </para>
/// </remarks>
internal sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BsftIdempotencyOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="options">Which header, which methods, and what is stored.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public IdempotencyMiddleware(RequestDelegate next, IOptions<BsftIdempotencyOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Invokes the middleware for the current request.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="gate">What decides, and what records.</param>
    /// <param name="observer">Where a decision is reported.</param>
    /// <returns>A task that completes when the request has been answered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="PreconditionRequiredException">
    /// Thrown when the endpoint requires a key and the request carries none.
    /// </exception>
    /// <exception cref="ConflictException">
    /// Thrown when the key names a different request, or a request that has not finished.
    /// </exception>
    public async Task InvokeAsync(HttpContext context, IIdempotencyGate gate, IIdempotencyObserver observer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(observer);

        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IIdempotencyMetadata>();

        if (metadata is null || !IsConsideredMethod(context.Request.Method))
        {
            await _next(context).ConfigureAwait(false);

            return;
        }

        var key = context.Request.Headers[_options.HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            await WithoutKeyAsync(context, metadata).ConfigureAwait(false);

            return;
        }

        IdempotencyKeyGuard.Ensure(key, _options.MaxKeyLength);

        await ProtectAsync(context, gate, observer, key).ConfigureAwait(false);
    }

    /// <summary>Reports whether this method is one the host asked to protect.</summary>
    /// <param name="method">The request method.</param>
    /// <returns><see langword="true"/> when the middleware considers it.</returns>
    /// <remarks>
    /// A loop rather than <c>Any</c> with a lambda: the lambda closes over the parameter and so
    /// allocates on every request that reaches a protected endpoint, for a walk over a list that
    /// usually holds one entry.
    /// </remarks>
    private bool IsConsideredMethod(string method)
    {
        foreach (var candidate in _options.Methods)
        {
            if (string.Equals(candidate, method, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Decides what an opted-in endpoint does when the header is absent.</summary>
    /// <param name="context">The request.</param>
    /// <param name="metadata">The endpoint's opt-in, which may override the host default.</param>
    /// <returns>A task that completes when the request has been answered or passed on.</returns>
    private async Task WithoutKeyAsync(HttpContext context, IIdempotencyMetadata metadata)
    {
        if (metadata.ShouldRequireKey ?? _options.ShouldRequireKey)
        {
            throw new PreconditionRequiredException(
                $"This operation requires an {_options.HeaderName} header carrying a key the client can "
                + "repeat, so that retrying it cannot run it twice.",
                IdempotencyDefaults.KeyRequiredErrorCode);
        }

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>Claims the key and acts on what the store answered.</summary>
    /// <param name="context">The request.</param>
    /// <param name="gate">What decides, and what records.</param>
    /// <param name="observer">Where a decision is reported.</param>
    /// <param name="key">The key the client sent.</param>
    /// <returns>A task that completes when the request has been answered.</returns>
    private async Task ProtectAsync(
        HttpContext context, IIdempotencyGate gate, IIdempotencyObserver observer, string key)
    {
        var result = await gate.BeginAsync(context, key, context.RequestAborted).ConfigureAwait(false);

        switch (result.Begin.Outcome)
        {
            case IdempotencyBeginOutcome.Replayed:
                await ReplayAsync(context, observer, result, key).ConfigureAwait(false);

                return;

            case IdempotencyBeginOutcome.FingerprintMismatch:
                await observer
                    .ReportAsync(IdempotencyAuditAction.KeyReused, result.Scope, key, context.RequestAborted)
                    .ConfigureAwait(false);

                throw new ConflictException(
                    $"This {_options.HeaderName} has already been used for a different request. Use a new key "
                    + "for a new request, and repeat a key only to retry the request it was minted for.",
                    IdempotencyDefaults.KeyReusedErrorCode);

            case IdempotencyBeginOutcome.InProgress:
                throw await PendingAsync(context, gate, observer, result, key).ConfigureAwait(false);

            default:
                await RunAsync(context, gate, result, key).ConfigureAwait(false);

                return;
        }
    }

    /// <summary>Answers with the stored response.</summary>
    /// <param name="context">The request.</param>
    /// <param name="observer">Where the decision is reported.</param>
    /// <param name="result">What the gate decided.</param>
    /// <param name="key">The key the client sent.</param>
    /// <returns>A task that completes when the response has been written.</returns>
    private static async Task ReplayAsync(
        HttpContext context, IIdempotencyObserver observer, IdempotencyGateResult result, string key)
    {
        await observer
            .ReportAsync(IdempotencyAuditAction.Replayed, result.Scope, key, context.RequestAborted)
            .ConfigureAwait(false);

        // Checked rather than asserted with `!`. The store is a public seam, and a host's own
        // implementation that answers Replayed with no response is a contract breach whose name should
        // appear in the log - not a NullReferenceException thrown from a middleware.
        var replayed = result.Begin.Response
            ?? throw new InvalidOperationException(
                $"The idempotency store reported a replay for scope '{result.Scope}' but carried no stored response. "
                + $"{nameof(IdempotencyBeginResult)}.{nameof(IdempotencyBeginResult.Response)} is required whenever the outcome is Replayed.");

        await IdempotencyReplayWriter
            .WriteAsync(context.Response, replayed, context.RequestAborted)
            .ConfigureAwait(false);
    }

    /// <summary>Refuses a request whose key is claimed by one that has not finished.</summary>
    /// <param name="context">The request.</param>
    /// <param name="gate">What works out how long the caller should wait.</param>
    /// <param name="observer">Where the decision is reported.</param>
    /// <param name="result">What the gate decided.</param>
    /// <param name="key">The key the client sent.</param>
    /// <returns>The conflict to throw, once the decision has been reported.</returns>
    /// <remarks>
    /// The header is set before the exception is thrown, because <c>BsftExceptionMiddleware</c>
    /// writes the status and the body but leaves the headers already on the response alone. The
    /// exception is returned for the caller to throw, so that the switch it is thrown from needs no
    /// unreachable <c>return</c> after it.
    /// </remarks>
    private async Task<ConflictException> PendingAsync(
        HttpContext context,
        IIdempotencyGate gate,
        IIdempotencyObserver observer,
        IdempotencyGateResult result,
        string key)
    {
        var seconds = gate.RetryAfterSeconds(result.Begin.LeaseExpiresAt);

        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        await observer
            .ReportAsync(IdempotencyAuditAction.InProgress, result.Scope, key, context.RequestAborted)
            .ConfigureAwait(false);

        return new ConflictException(
            string.Format(
                CultureInfo.InvariantCulture,
                "A request with this idempotency key is still running. Retry it in {0} seconds; retrying "
                + "is safe, because it will either replay the first response or run once.",
                seconds),
            IdempotencyDefaults.InProgressErrorCode);
    }

    /// <summary>Runs the request under a reservation and stores what it answered.</summary>
    /// <param name="context">The request.</param>
    /// <param name="gate">What records the outcome.</param>
    /// <param name="result">What the gate decided, carrying the scope and the fingerprint.</param>
    /// <param name="key">The key the client sent.</param>
    /// <returns>A task that completes when the response has been written.</returns>
    private async Task RunAsync(
        HttpContext context, IIdempotencyGate gate, IdempotencyGateResult result, string key)
    {
        using var buffer = new ResponseBodyBuffer(context.Response);

        IdempotencyResponseSnapshot? snapshot = null;

        try
        {
            await _next(context).ConfigureAwait(false);

            // Asked before the snapshot is taken, not after: a response that will not be stored — a
            // server error, an excluded status, one over the bound — would otherwise pay for a copy
            // of its whole body and a header dictionary that are thrown away in the next breath.
            if (gate.IsReplayable(context.Response.StatusCode, buffer.Length))
                snapshot = buffer.Capture(_options.CachedResponseHeaders);
        }
        catch
        {
            // A request that threw leaves no snapshot, and the reservation goes with it: the client
            // asked to be allowed to retry, and a record that outlived the failure would refuse
            // exactly that.
            await ReleaseOrCompleteAsync(gate, result, key, snapshot: null).ConfigureAwait(false);

            throw;
        }

        try
        {
            // The response goes out before the record is written, and that order is the point. The
            // work the endpoint did is done either way; a store that cannot be reached afterwards
            // costs the deployment a remembered response, while the reverse order would cost the
            // client a response it had already earned and invite it to retry work that succeeded.
            await buffer.ReleaseAsync(context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            // CancellationToken.None because a disconnected client is the case where finishing the
            // bookkeeping matters most: the reservation outlives the socket either way.
            await ReleaseOrCompleteAsync(gate, result, key, snapshot).ConfigureAwait(false);
        }
    }

    /// <summary>Stores a replayable response, or drops the reservation.</summary>
    /// <param name="gate">What records the outcome.</param>
    /// <param name="result">What the gate decided, carrying the scope and the fingerprint.</param>
    /// <param name="key">The key the client sent.</param>
    /// <param name="snapshot">
    /// What the request answered, or null when it threw or answered something not worth storing.
    /// </param>
    /// <returns>A task that completes when the record has been written or removed.</returns>
    private static async Task ReleaseOrCompleteAsync(
        IIdempotencyGate gate,
        IdempotencyGateResult result,
        string key,
        IdempotencyResponseSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            var completion = new IdempotencyCompletionRequest(
                result.Scope, key, result.Fingerprint, snapshot, gate.ReplayableUntil);

            await gate.CompleteAsync(completion, CancellationToken.None).ConfigureAwait(false);

            return;
        }

        await gate
            .ReleaseAsync(result.Scope, key, result.Fingerprint, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
