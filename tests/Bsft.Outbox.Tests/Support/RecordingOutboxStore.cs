namespace Bsft.Outbox.Tests.Support;

/// <summary>
/// A store that hands out what a test staged and records every completion, so a pass can be asserted
/// on what it decided rather than on what a database ended up holding.
/// </summary>
public sealed class RecordingOutboxStore : IOutboxStore<string>
{
    /// <summary>The rows the next lease hands out.</summary>
    public List<LeasedOutboxRow<string>> Pending { get; } = [];

    /// <summary>Whether a completion is accepted, which is what an expired lease answers with.</summary>
    public bool MarkSucceeds { get; set; } = true;

    /// <summary>The ids marked processed, in order.</summary>
    public List<long> Processed { get; } = [];

    /// <summary>The abandonments, in order.</summary>
    public List<(long Id, DateTimeOffset NextAttemptAt)> Abandoned { get; } = [];

    /// <summary>The retirements, in order.</summary>
    public List<(long Id, string Reason)> DeadLettered { get; } = [];

    /// <summary>What each lease was asked for, in order.</summary>
    public List<(DateTimeOffset Now, TimeSpan LeaseDuration, int BatchSize)> Leases { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<LeasedOutboxRow<string>>> LeaseAsync(
        DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken ct = default)
    {
        Leases.Add((now, leaseDuration, batchSize));

        IReadOnlyList<LeasedOutboxRow<string>> claimed = Pending.Take(batchSize).ToList();

        return Task.FromResult(claimed);
    }

    /// <inheritdoc />
    public Task<bool> TryMarkProcessedAsync(
        long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken ct = default)
    {
        Processed.Add(id);

        return Task.FromResult(MarkSucceeds);
    }

    /// <inheritdoc />
    public Task AbandonAsync(long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken ct = default)
    {
        Abandoned.Add((id, nextAttemptAt));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeadLetterAsync(
        long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default)
    {
        DeadLettered.Add((id, reason));

        return Task.CompletedTask;
    }
}
