namespace Bsft.Retention.Tests.Fakes;

/// <summary>
/// A retention pass over records that carry an absolute expiry instant, purging those whose
/// expiry is at or before the cutoff it is handed.
/// </summary>
/// <remarks>
/// Public for the same reason as <see cref="RecordingRetentionPass"/>: the sweeper's logger is
/// generic over the pass type.
/// </remarks>
public sealed class ExpiringRecordsRetentionPass : IRetentionPass
{
    /// <summary>Gets the expiry instants of the records still stored.</summary>
    public List<DateTimeOffset> ExpiresAt { get; } = [];

    /// <summary>Gets the cutoff of every batch, in order.</summary>
    public List<DateTimeOffset> Cutoffs { get; } = [];

    /// <summary>Gets or sets what runs after each batch, such as a test advancing the clock.</summary>
    public Action? AfterBatch { get; set; }

    public Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken ct = default)
    {
        Cutoffs.Add(olderThan);

        var purged = ExpiresAt.Where(expiry => expiry <= olderThan).Take(batchSize).ToList();

        foreach (var expiry in purged)
            ExpiresAt.Remove(expiry);

        AfterBatch?.Invoke();

        return Task.FromResult(purged.Count);
    }
}
