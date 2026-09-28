using Bsft.Idempotency.Diagnostics;

namespace Bsft.Idempotency.Auditing;

/// <summary>
/// Counts the decision and hands it to the host's audit sink.
/// </summary>
internal sealed class IdempotencyObserver : IIdempotencyObserver
{
    private readonly IIdempotencyAuditSink _auditSink;
    private readonly IIdempotencyTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyObserver"/> class.
    /// </summary>
    /// <param name="auditSink">Where an audited decision goes.</param>
    /// <param name="telemetry">The counters.</param>
    /// <param name="timeProvider">The clock the event is stamped from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public IdempotencyObserver(
        IIdempotencyAuditSink auditSink, IIdempotencyTelemetry telemetry, TimeProvider timeProvider)
    {
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope"/> or <paramref name="key"/> is null.</exception>
    public Task ReportAsync(
        IdempotencyAuditAction action, string scope, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);

        _telemetry.Record(action);

        return _auditSink.RecordAsync(
            new IdempotencyAuditEvent(action, scope, key, _timeProvider.GetUtcNow()), cancellationToken);
    }
}
