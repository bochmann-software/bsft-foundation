using System.Collections.Concurrent;

namespace Bsft.Scheduling.StoredSchedules;

/// <summary>
/// In-memory <see cref="IStoredScheduleStore"/> used when no persistent store is configured. Schedules
/// are lost when the process ends — use the EF Core store for a catalog that has to survive one.
/// </summary>
/// <remarks>
/// Reads hand out copies rather than the stored instances, matching what a database-backed store
/// necessarily does; see <see cref="Bsft.Scheduling.InMemoryJobStore"/> for the same reasoning.
/// </remarks>
public sealed class InMemoryStoredScheduleStore : IStoredScheduleStore
{
    private readonly ConcurrentDictionary<long, StoredSchedule> _schedules = new();
    private readonly object _gate = new();
    private long _nextId;

    /// <inheritdoc />
    public Task<long> CreateAsync(StoredSchedule schedule, CancellationToken cancellationToken = default)
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        lock (_gate)
        {
            var id = Interlocked.Increment(ref _nextId);
            var stored = schedule.Copy();
            stored.Id = id;
            stored.ConcurrencyToken = Guid.NewGuid();
            _schedules[id] = stored;

            schedule.Id = id;
            schedule.ConcurrencyToken = stored.ConcurrencyToken;
            return Task.FromResult(id);
        }
    }

    /// <inheritdoc />
    public Task<StoredSchedule?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_schedules.TryGetValue(id, out var stored) ? stored.Copy() : null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredSchedule>> ListByTenantAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<StoredSchedule> result = _schedules.Values
                .Where(s => tenantId is null || string.Equals(s.TenantId, tenantId, StringComparison.Ordinal))
                .OrderBy(s => s.Id)
                .Select(s => s.Copy())
                .ToList();

            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryUpdateAsync(StoredSchedule schedule, CancellationToken cancellationToken = default)
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        lock (_gate)
        {
            if (!_schedules.TryGetValue(schedule.Id, out var stored))
                return Task.FromResult(false);

            stored.JobKey = schedule.JobKey;
            stored.Schedule = schedule.Schedule;
            stored.Payload = schedule.Payload;
            stored.NextRunAt = schedule.NextRunAt;
            stored.UpdatedAt = schedule.UpdatedAt;
            stored.ConcurrencyToken = Guid.NewGuid();
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> TrySetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_schedules.TryGetValue(id, out var stored))
                return Task.FromResult(false);

            stored.Enabled = enabled;
            stored.ConcurrencyToken = Guid.NewGuid();
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_schedules.TryRemove(id, out _));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredSchedule>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<StoredSchedule> due = _schedules.Values
                .Where(s => s.Enabled && s.NextRunAt is not null && s.NextRunAt <= now)
                .OrderBy(s => s.NextRunAt)
                .ThenBy(s => s.Id)
                .Take(batchSize)
                .Select(s => s.Copy())
                .ToList();

            return Task.FromResult(due);
        }
    }

    /// <inheritdoc />
    public Task<StoredSchedule?> AdvanceOrNullAsync(
        StoredSchedule schedule, DateTimeOffset ranAt, DateTimeOffset? nextRunAt, CancellationToken cancellationToken = default)
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        lock (_gate)
        {
            if (!_schedules.TryGetValue(schedule.Id, out var stored) || stored.ConcurrencyToken != schedule.ConcurrencyToken)
                return Task.FromResult<StoredSchedule?>(null);

            stored.LastRunAt = ranAt;
            stored.NextRunAt = nextRunAt;
            stored.UpdatedAt = ranAt;
            stored.ConcurrencyToken = Guid.NewGuid();

            return Task.FromResult<StoredSchedule?>(stored.Copy());
        }
    }
}
