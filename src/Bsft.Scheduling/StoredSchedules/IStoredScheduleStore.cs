namespace Bsft.Scheduling.StoredSchedules;

/// <summary>
/// Persistence for <see cref="StoredSchedule"/> rows, including the atomic advance that lets several
/// application instances share one catalog without ever turning the same due slot into two runs.
/// </summary>
/// <remarks>
/// <see cref="AdvanceOrNullAsync"/> is the only member that has to be atomic, in the same sense that
/// <see cref="IJobStore.ClaimAsync"/> is: it must move a schedule's <see cref="StoredSchedule.NextRunAt"/>
/// forward in a single conditional operation guarded by <see cref="StoredSchedule.ConcurrencyToken"/>,
/// so that exactly one caller wins the due slot. Everything else may be implemented as ordinary reads
/// and writes.
/// </remarks>
public interface IStoredScheduleStore
{
    /// <summary>
    /// Persists a new schedule and returns its assigned identity.
    /// </summary>
    /// <param name="schedule">The schedule to store.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The identity assigned to the schedule.</returns>
    Task<long> CreateAsync(StoredSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single schedule by its identity.
    /// </summary>
    /// <param name="id">The identity of the schedule.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The schedule, or <c>null</c> when no schedule with that identity exists.</returns>
    Task<StoredSchedule?> FindAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every schedule belonging to a tenant, or every schedule when <paramref name="tenantId"/>
    /// is <c>null</c>.
    /// </summary>
    /// <param name="tenantId">The tenant to filter by, or <c>null</c> for every tenant.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The matching schedules, oldest first.</returns>
    Task<IReadOnlyList<StoredSchedule>> ListByTenantAsync(string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the job key, recurrence rule, payload and next-run instant of an existing schedule.
    /// </summary>
    /// <param name="schedule">The schedule carrying the new values, keyed by <see cref="StoredSchedule.Id"/>.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when a schedule with that identity was found and updated.</returns>
    Task<bool> TryUpdateAsync(StoredSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches a schedule on or off.
    /// </summary>
    /// <param name="id">The identity of the schedule.</param>
    /// <param name="enabled">The new enabled state.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when a schedule with that identity was found.</returns>
    Task<bool> TrySetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a schedule.
    /// </summary>
    /// <param name="id">The identity of the schedule to remove.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when a schedule with that identity was found and removed.</returns>
    Task<bool> TryDeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the enabled schedules whose next occurrence is due.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="batchSize">The maximum number of schedules to return.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The due schedules, oldest due first.</returns>
    Task<IReadOnlyList<StoredSchedule>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to advance a schedule past the due slot it was read with.
    /// </summary>
    /// <param name="schedule">The schedule as read by <see cref="ListDueAsync"/>, carrying the token the advance is checked against.</param>
    /// <param name="ranAt">The instant recorded as <see cref="StoredSchedule.LastRunAt"/>.</param>
    /// <param name="nextRunAt">
    /// The instant of the following occurrence, or <c>null</c> when the recurrence rule has none.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// The advanced schedule carrying its new concurrency token, or <c>null</c> when another instance
    /// already claimed the slot or the schedule no longer exists.
    /// </returns>
    Task<StoredSchedule?> AdvanceOrNullAsync(
        StoredSchedule schedule, DateTimeOffset ranAt, DateTimeOffset? nextRunAt, CancellationToken cancellationToken = default);
}
