namespace Bsft.Idempotency;

/// <summary>
/// What a finished request asks a store to store against its reservation.
/// </summary>
/// <param name="Scope">The caller the key belongs to, as it was reserved.</param>
/// <param name="Key">The value the client sent in the <c>Idempotency-Key</c> header.</param>
/// <param name="Fingerprint">
/// The digest of the request that reserved the key, which is the proof that the row being completed
/// is this request's own. The state alone is not that proof: a request whose lease lapsed while it
/// was still running would otherwise complete over the reservation of whoever reclaimed the key,
/// storing a response under somebody else's fingerprint.
/// </param>
/// <param name="Response">The status, allowlisted headers and body a replay will answer with.</param>
/// <param name="ExpiresAt">
/// When the stored response stops being replayed, from
/// <see cref="BsftIdempotencyOptions.Retention"/>. It is a contract rather than an implementation
/// detail: after it, the same key runs again.
/// </param>
public sealed record IdempotencyCompletionRequest(
    string Scope,
    string Key,
    string Fingerprint,
    IdempotencyResponseSnapshot Response,
    DateTimeOffset ExpiresAt);
