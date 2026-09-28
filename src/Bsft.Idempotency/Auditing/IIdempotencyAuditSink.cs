namespace Bsft.Idempotency.Auditing;

/// <summary>
/// Receives the idempotency decisions worth recording.
/// </summary>
/// <remarks>
/// A seam rather than a dependency on the audit package, so this package does not decide where an
/// application keeps its audit trail. The default discards what it is given, which means switching
/// an audit trail on is a registration rather than a change to the middleware, and forgetting to
/// register one cannot break a request.
/// </remarks>
public interface IIdempotencyAuditSink
{
    /// <summary>
    /// Records one event.
    /// </summary>
    /// <param name="auditEvent">What happened.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the event has been handed on.</returns>
    Task RecordAsync(IdempotencyAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
