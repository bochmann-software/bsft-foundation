namespace Bsft.Idempotency;

/// <summary>
/// What a request asks a store to reserve: which caller, which key, which request, and how long the
/// reservation stands before it is treated as abandoned.
/// </summary>
/// <param name="Scope">
/// The caller the key belongs to — the tenant, the authenticated subject and the client id, reduced
/// to one bounded value. It leads the store's unique index, and it is what stops one tenant reading
/// another's response by guessing a key.
/// </param>
/// <param name="Key">The value the client sent in the <c>Idempotency-Key</c> header.</param>
/// <param name="Fingerprint">
/// The digest of the request itself. A key presented again with a different fingerprint is a
/// mistake on the client's side and is refused rather than answered from the store.
/// </param>
/// <param name="LeaseExpiresAt">
/// When an unfinished reservation stops refusing concurrent requests. It is how a process torn down
/// mid-request stops blocking its own key for ever.
/// </param>
public sealed record IdempotencyReservationRequest(
    string Scope,
    string Key,
    string Fingerprint,
    DateTimeOffset LeaseExpiresAt);
