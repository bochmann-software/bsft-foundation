using Bsft.Idempotency.Auditing;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>An observer that keeps what it was told.</summary>
internal sealed class RecordingObserver : IIdempotencyObserver
{
    public List<IdempotencyAuditAction> Actions { get; } = [];

    public Task ReportAsync(
        IdempotencyAuditAction action, string scope, string key, CancellationToken ct = default)
    {
        Actions.Add(action);

        return Task.CompletedTask;
    }
}
