namespace Bsft.Idempotency.Replay;

/// <summary>
/// What the gate decided, and the caller digest the rest of the request needs to finish the record.
/// </summary>
/// <param name="Scope">The caller digest the key was claimed under.</param>
/// <param name="Fingerprint">The digest of the request, which the completion is checked against.</param>
/// <param name="Begin">What the store answered.</param>
internal sealed record IdempotencyGateResult(
    string Scope, string Fingerprint, IdempotencyBeginResult Begin);
