namespace Bsft.Idempotency;

/// <summary>
/// What happened when a request tried to claim its key.
/// </summary>
public enum IdempotencyBeginOutcome
{
    /// <summary>
    /// The key was free and is now reserved by this request, which may proceed. Exactly one
    /// concurrent request under a key ever gets this answer.
    /// </summary>
    Started = 0,

    /// <summary>
    /// The key names a completed request with the same fingerprint, and its stored response is the
    /// answer. Nothing runs.
    /// </summary>
    Replayed = 1,

    /// <summary>
    /// The key is known but the request behind it is not this one. Serving the stored response would
    /// answer a question the caller did not ask, so the caller is told instead — 409,
    /// <see cref="IdempotencyDefaults.KeyReusedErrorCode"/>.
    /// </summary>
    FingerprintMismatch = 2,

    /// <summary>
    /// The key is reserved by a request that has not finished. The caller is told to come back —
    /// 409 with <c>Retry-After</c>, <see cref="IdempotencyDefaults.InProgressErrorCode"/> — rather
    /// than being blocked, which would hold a connection for the duration of somebody else's work.
    /// </summary>
    InProgress = 3,
}
