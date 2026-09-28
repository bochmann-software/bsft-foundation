using Bsft.Idempotency.Auditing;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>Keeps every event it is handed, so a test can assert what was audited.</summary>
internal sealed class RecordingAuditSink : IIdempotencyAuditSink
{
    private readonly List<IdempotencyAuditEvent> _events = [];

    public IReadOnlyList<IdempotencyAuditEvent> Events
    {
        get
        {
            lock (_events)
                return _events.ToList();
        }
    }

    public Task RecordAsync(IdempotencyAuditEvent auditEvent, CancellationToken ct = default)
    {
        lock (_events)
            _events.Add(auditEvent);

        return Task.CompletedTask;
    }
}
