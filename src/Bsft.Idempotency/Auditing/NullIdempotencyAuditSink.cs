namespace Bsft.Idempotency.Auditing;

/// <summary>
/// The default <see cref="IIdempotencyAuditSink"/>, which discards what it is given.
/// </summary>
/// <remarks>
/// The events are emitted whether or not anybody listens, so a host that wants a key-reuse conflict
/// in its audit trail registers a sink and changes nothing else.
/// </remarks>
public sealed class NullIdempotencyAuditSink : IIdempotencyAuditSink
{
    /// <inheritdoc />
    public Task RecordAsync(IdempotencyAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
