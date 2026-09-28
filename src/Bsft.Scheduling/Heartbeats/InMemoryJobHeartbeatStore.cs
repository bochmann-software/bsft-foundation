using System.Collections.Concurrent;

namespace Bsft.Scheduling.Heartbeats;

/// <summary>
/// Default <see cref="IJobHeartbeatStore"/>: the last successful run of each job on this instance,
/// held in memory.
/// </summary>
public sealed class InMemoryJobHeartbeatStore : IJobHeartbeatStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastCompleted =
        new(StringComparer.Ordinal);

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryJobHeartbeatStore"/> class.
    /// </summary>
    /// <param name="timeProvider">Supplies the instants recorded and the observation baseline.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    /// <remarks>
    /// The store owns the clock rather than taking an instant per call, so that every heartbeat in a
    /// deployment is stamped from one source and a test drives them all from virtual time.
    /// </remarks>
    public InMemoryJobHeartbeatStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ObservingSince = _timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public DateTimeOffset ObservingSince { get; }

    /// <inheritdoc />
    public Task RecordCompletedAsync(string jobName, CancellationToken cancellationToken = default)
    {
        if (jobName is null)
            throw new ArgumentNullException(nameof(jobName));

        var completedAt = _timeProvider.GetUtcNow();

        // The later instant wins rather than the last writer. Two overlapping executions of the same
        // job may settle out of order, and a heartbeat that moved backwards would report a job as
        // later than it is.
        _lastCompleted.AddOrUpdate(
            jobName,
            completedAt,
            (_, existing) => existing > completedAt ? existing : completedAt);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> LastCompletedAtAsync(string jobName, CancellationToken cancellationToken = default)
    {
        if (jobName is null)
            throw new ArgumentNullException(nameof(jobName));

        return Task.FromResult(_lastCompleted.TryGetValue(jobName, out var completedAt) ? completedAt : null as DateTimeOffset?);
    }
}
