using Bsft.Messaging.Outbox;

namespace Bsft.Messaging.Tests.Fakes;

public sealed class FakeOutboxStore : IOutboxStore
{
    private readonly Queue<IReadOnlyList<LeasedOutboxMessage>> _batches = new();

    public List<(long Id, Guid LeaseToken)> Marked { get; } = [];

    public List<(long Id, Guid LeaseToken, DateTimeOffset NextAttemptAt)> Abandoned { get; } = [];

    public List<(long Id, Guid LeaseToken, string Reason)> DeadLettered { get; } = [];

    public DateTimeOffset LastDeadLetterInstant { get; private set; }

    public int LeaseCalls { get; private set; }

    public TimeSpan LastLeaseDuration { get; private set; }

    public int LastBatchSize { get; private set; }

    public DateTimeOffset LastLeaseInstant { get; private set; }

    public DateTimeOffset LastProcessedInstant { get; private set; }

    public bool MarkSucceeds { get; set; } = true;

    public Exception? LeaseFailure { get; set; }

    public Action<int>? OnLease { get; set; }

    public void EnqueueBatch(params LeasedOutboxMessage[] batch) => _batches.Enqueue(batch);

    public Task<IReadOnlyList<LeasedOutboxMessage>> LeaseAsync(
        DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken ct = default)
    {
        LeaseCalls++;
        LastLeaseInstant = now;
        LastLeaseDuration = leaseDuration;
        LastBatchSize = batchSize;
        OnLease?.Invoke(LeaseCalls);

        if (LeaseFailure is not null)
            return Task.FromException<IReadOnlyList<LeasedOutboxMessage>>(LeaseFailure);

        return Task.FromResult(_batches.Count > 0 ? _batches.Dequeue() : (IReadOnlyList<LeasedOutboxMessage>)[]);
    }

    public Task<bool> TryMarkProcessedAsync(
        long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken ct = default)
    {
        LastProcessedInstant = processedAt;
        Marked.Add((id, leaseToken));

        return Task.FromResult(MarkSucceeds);
    }

    public Task AbandonAsync(
        long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken ct = default)
    {
        Abandoned.Add((id, leaseToken, nextAttemptAt));

        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(
        long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default)
    {
        DeadLettered.Add((id, leaseToken, reason));
        LastDeadLetterInstant = deadLetteredAt;

        return Task.CompletedTask;
    }
}
