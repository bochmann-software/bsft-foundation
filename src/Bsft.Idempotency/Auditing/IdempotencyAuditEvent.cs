namespace Bsft.Idempotency.Auditing;

/// <summary>
/// One thing that happened to an idempotency key, as an audit sink sees it.
/// </summary>
/// <param name="Action">Which of the three reported decisions this is.</param>
/// <param name="Scope">
/// The caller the key belongs to, already reduced to its bounded digest. The digest rather than the
/// tenant and subject in the clear, so an audit trail of key activity is not a second place the
/// identity of every caller is written.
/// </param>
/// <param name="Key">The key the client sent.</param>
/// <param name="OccurredAt">When it happened, read from <see cref="TimeProvider"/>.</param>
public sealed record IdempotencyAuditEvent(
    IdempotencyAuditAction Action,
    string Scope,
    string Key,
    DateTimeOffset OccurredAt);
