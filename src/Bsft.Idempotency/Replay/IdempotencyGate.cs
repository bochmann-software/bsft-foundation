using Bsft.Idempotency.Fingerprinting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Bsft.Idempotency.Replay;

/// <summary>
/// The decision half of the middleware: fingerprint, claim, complete or release.
/// </summary>
internal sealed class IdempotencyGate : IIdempotencyGate
{
    /// <summary>The smallest honest <c>Retry-After</c>; zero would invite an immediate retry storm.</summary>
    private const int MinimumRetryAfterSeconds = 1;

    /// <summary>The lowest status that is a server error, none of which is ever stored.</summary>
    private const int LowestServerErrorStatus = 500;

    private readonly IIdempotencyStore _store;
    private readonly IIdempotencyFingerprinter _fingerprinter;
    private readonly BsftIdempotencyOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyGate"/> class.
    /// </summary>
    /// <param name="store">Where records live.</param>
    /// <param name="fingerprinter">What reduces a request to its two digests.</param>
    /// <param name="options">The retention, the lease and the replay policy.</param>
    /// <param name="timeProvider">The clock every expiry is read from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public IdempotencyGate(
        IIdempotencyStore store,
        IIdempotencyFingerprinter fingerprinter,
        IOptions<BsftIdempotencyOptions> options,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _fingerprinter = fingerprinter ?? throw new ArgumentNullException(nameof(fingerprinter));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> or <paramref name="key"/> is null.</exception>
    public async ValueTask<IdempotencyGateResult> BeginAsync(
        HttpContext context, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(key);

        var fingerprint = await _fingerprinter.ComputeAsync(context, cancellationToken).ConfigureAwait(false);
        var lease = _timeProvider.GetUtcNow() + _options.InProgressTimeout;

        var begin = await _store
            .BeginAsync(
                new IdempotencyReservationRequest(fingerprint.Scope, key, fingerprint.Fingerprint, lease), cancellationToken)
            .ConfigureAwait(false);

        return new IdempotencyGateResult(fingerprint.Scope, fingerprint.Fingerprint, begin);
    }

    /// <inheritdoc />
    public DateTimeOffset ReplayableUntil => _timeProvider.GetUtcNow() + _options.Retention;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public Task CompleteAsync(IdempotencyCompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _store.CompleteAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task ReleaseAsync(
        string scope, string key, string fingerprint, CancellationToken cancellationToken = default) =>
        _store.ReleaseAsync(scope, key, fingerprint, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A server error is never stored, which is the whole point of the header: the client asked to
    /// be allowed to retry, and it is. A client error is stored unless the host listed it, because
    /// replaying a business refusal is cheaper and more truthful than re-running the request that
    /// earned it — while a 401 or a 429 may have been fixed by the caller in between. A response
    /// larger than the store is willing to hold is not stored either, so one caller cannot grow the
    /// record table without limit by repeatedly asking for a large answer.
    /// </remarks>
    public bool IsReplayable(int statusCode, long bodyLength) =>
        statusCode < LowestServerErrorStatus
        && bodyLength <= _options.MaxResponseBytes
        && !_options.NonReplayableStatusCodes.Contains(statusCode);

    /// <inheritdoc />
    /// <remarks>
    /// The configured wait, capped by what is left of the lease and floored at a second: a client is
    /// never told to wait a minute for a handler that takes 200 ms, and never told to come back
    /// after the point at which the key is already retryable.
    /// </remarks>
    public int RetryAfterSeconds(DateTimeOffset? leaseExpiresAt)
    {
        var wait = _options.InProgressRetryAfter;

        if (leaseExpiresAt is { } lease)
        {
            var remaining = lease - _timeProvider.GetUtcNow();

            if (remaining < wait)
                wait = remaining;
        }

        var seconds = (int)Math.Ceiling(wait.TotalSeconds);

        return seconds < MinimumRetryAfterSeconds ? MinimumRetryAfterSeconds : seconds;
    }
}
