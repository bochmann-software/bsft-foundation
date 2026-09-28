using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Replay;

/// <summary>
/// Turns a request and a key into a decision, and records what the request answered.
/// </summary>
/// <remarks>
/// It sits between the middleware and the store so the middleware is left with the HTTP work —
/// reading a header, buffering a response, choosing a status — and the fingerprinting, the clock and
/// the retention live in one place that a test can drive without a pipeline.
/// </remarks>
internal interface IIdempotencyGate
{
    /// <summary>Fingerprints the request and tries to claim its key.</summary>
    /// <param name="context">The request.</param>
    /// <param name="key">The key the client sent.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The caller digest and what the store answered.</returns>
    ValueTask<IdempotencyGateResult> BeginAsync(
        HttpContext context, string key, CancellationToken cancellationToken = default);

    /// <summary>Stores the response of a request that finished.</summary>
    /// <param name="request">The reservation to complete and what to store against it.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the record has been written.</returns>
    Task CompleteAsync(IdempotencyCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the instant a response stored now stops being replayed, which is the retention measured
    /// from the current time.
    /// </summary>
    /// <remarks>
    /// On the gate rather than worked out by the caller, because the retention and the clock are
    /// both configuration this owns and neither belongs in the middleware.
    /// </remarks>
    DateTimeOffset ReplayableUntil { get; }

    /// <summary>Drops a reservation so the key is free again.</summary>
    /// <param name="scope">The caller digest the key was claimed under.</param>
    /// <param name="key">The key the client sent.</param>
    /// <param name="fingerprint">The digest the key was reserved with.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the reservation is gone.</returns>
    Task ReleaseAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>Reports whether a response is stored for replay or released.</summary>
    /// <param name="statusCode">The status the request answered with.</param>
    /// <param name="bodyLength">How many bytes the response body is.</param>
    /// <returns><see langword="true"/> when the response may be replayed.</returns>
    bool IsReplayable(int statusCode, long bodyLength);

    /// <summary>Works out what a caller refused with an in-progress 409 should wait.</summary>
    /// <param name="leaseExpiresAt">When the standing reservation lapses, when the store said.</param>
    /// <returns>The whole seconds to put in <c>Retry-After</c>.</returns>
    int RetryAfterSeconds(DateTimeOffset? leaseExpiresAt);
}
