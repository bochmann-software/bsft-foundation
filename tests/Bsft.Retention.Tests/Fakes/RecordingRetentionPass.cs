namespace Bsft.Retention.Tests.Fakes;

/// <summary>
/// A retention pass that deletes from a counted backlog and records every call it was given.
/// </summary>
/// <remarks>
/// It behaves the way a real pass does — it honours the batch size and reports what it deleted — so a
/// sweeper test can assert the batching and the drain against a pass that plays by the contract,
/// rather than against a stub that returns a canned number.
/// </remarks>
/// <remarks>
/// Public rather than internal because the sweeper's logger is generic over the pass type, and a
/// substitute for <c>ILogger&lt;RetentionSweeper&lt;TPass&gt;&gt;</c> cannot be proxied while
/// <c>TPass</c> is invisible to the proxy assembly.
/// </remarks>
public sealed class RecordingRetentionPass : IRetentionPass
{
    private readonly List<(int BatchSize, DateTimeOffset OlderThan)> _calls = [];

    /// <summary>Gets or sets how many records are still waiting to be deleted.</summary>
    public int Backlog { get; set; }

    /// <summary>Gets or sets a failure the next call throws instead of deleting.</summary>
    public Exception? Failure { get; set; }

    /// <summary>The calls the sweeper made, in order.</summary>
    public IReadOnlyList<(int BatchSize, DateTimeOffset OlderThan)> Calls
    {
        get
        {
            lock (_calls)
                return _calls.ToList();
        }
    }

    public Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken ct = default)
    {
        lock (_calls)
            _calls.Add((batchSize, olderThan));

        if (Failure is { } failure)
            throw failure;

        var purged = Math.Min(batchSize, Backlog);
        Backlog -= purged;

        return Task.FromResult(purged);
    }
}
