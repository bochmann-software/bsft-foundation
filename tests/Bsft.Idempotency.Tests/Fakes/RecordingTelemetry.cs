using Bsft.Idempotency.Diagnostics;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>Remembers every decision it was told about.</summary>
internal sealed class RecordingTelemetry : IIdempotencyTelemetry
{
    public List<IdempotencyAuditAction> Actions { get; } = [];

    public void Record(IdempotencyAuditAction action) => Actions.Add(action);
}
