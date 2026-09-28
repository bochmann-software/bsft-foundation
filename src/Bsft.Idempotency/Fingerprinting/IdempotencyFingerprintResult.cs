namespace Bsft.Idempotency.Fingerprinting;

/// <summary>
/// The two digests a request is recognised by: who is asking, and what they are asking for.
/// </summary>
/// <param name="Scope">
/// The caller — tenant, authenticated subject and client id — reduced to one bounded digest. It is
/// part of the store key, which is what makes one tenant's key unreachable from another's session
/// even when the key value is guessed exactly.
/// </param>
/// <param name="Fingerprint">
/// The request — method, matched route, sorted query, caller, content type and the digest of the
/// body. A key presented again with a different fingerprint is refused rather than answered.
/// </param>
internal sealed record IdempotencyFingerprintResult(string Scope, string Fingerprint);
