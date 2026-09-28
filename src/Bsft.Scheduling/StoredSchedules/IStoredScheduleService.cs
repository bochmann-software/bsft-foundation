namespace Bsft.Scheduling.StoredSchedules;

/// <summary>
/// Manages the persisted, per-tenant recurring-schedule catalog: create, edit, enable, disable and
/// delete a schedule against a registered job, validated against <see cref="IJobRegistry"/> so a typo
/// in the job key fails at the call rather than producing a schedule that never runs.
/// </summary>
/// <remarks>
/// Every member that addresses one schedule names the tenant it is acting for as well as the
/// identity, and answers as if a schedule owned by anybody else did not exist. Identities are handed
/// out by the store and are sequential, so an id alone is a guessable handle: taking one without the
/// tenant would let a caller who may reach its own schedule read, rewrite, switch off or delete every
/// other tenant's by counting. A <c>null</c> tenant addresses the schedules that belong to no tenant,
/// the ones <see cref="CreateAsync"/> writes for a <c>null</c> tenant; it is not a skeleton key. A
/// host administering the catalog across tenants reads the owner from
/// <see cref="ListAsync"/> and acts under it.
/// </remarks>
public interface IStoredScheduleService
{
    /// <summary>
    /// Creates a new schedule and computes its first occurrence.
    /// </summary>
    /// <param name="tenantId">The tenant the schedule belongs to, or <c>null</c> for one that belongs to none.</param>
    /// <param name="jobKey">The registered job name the schedule enqueues.</param>
    /// <param name="schedule">The recurrence rule.</param>
    /// <param name="payload">The opaque payload handed to the job on every occurrence, or <c>null</c>.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The created schedule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobKey"/> or <paramref name="schedule"/> is null.</exception>
    /// <exception cref="JobNotRegisteredException">Thrown when <paramref name="jobKey"/> names no registered job.</exception>
    Task<StoredSchedule> CreateAsync(
        string? tenantId, string jobKey, JobSchedule schedule, string? payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single schedule by its identity.
    /// </summary>
    /// <param name="tenantId">The tenant the caller is acting for, or <c>null</c> for a schedule that belongs to none.</param>
    /// <param name="id">The identity of the schedule.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// The schedule, or <c>null</c> when that identity names no schedule of that tenant.
    /// </returns>
    Task<StoredSchedule?> FindAsync(string? tenantId, long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every schedule belonging to a tenant, or every schedule when <paramref name="tenantId"/>
    /// is <c>null</c>.
    /// </summary>
    /// <param name="tenantId">The tenant to filter by, or <c>null</c> for every tenant.</param>
    /// <remarks>
    /// This is the one member where <c>null</c> means every tenant rather than the tenantless ones, and
    /// deliberately so: it reads nothing it was not already given a catalog-wide view for, and a host
    /// administering across tenants needs somewhere to learn which owners exist. It is the members that
    /// act on one identity that would turn a wildcard into a way through, so those take <c>null</c> as
    /// an owner like any other. A host that exposes this to a tenant passes that tenant's own id.
    /// </remarks>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The matching schedules, oldest first.</returns>
    Task<IReadOnlyList<StoredSchedule>> ListAsync(string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the recurrence rule and payload of an existing schedule and recomputes its next
    /// occurrence from the current instant, so an edited schedule follows its new rule rather than
    /// replaying whatever was due under the old one.
    /// </summary>
    /// <param name="tenantId">The tenant the caller is acting for, or <c>null</c> for a schedule that belongs to none.</param>
    /// <param name="id">The identity of the schedule to edit.</param>
    /// <param name="schedule">The new recurrence rule.</param>
    /// <param name="payload">The new payload, or <c>null</c>.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when that identity named a schedule of that tenant and it was updated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="schedule"/> is null.</exception>
    Task<bool> TryUpdateAsync(
        string? tenantId, long id, JobSchedule schedule, string? payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches a schedule on or off.
    /// </summary>
    /// <param name="tenantId">The tenant the caller is acting for, or <c>null</c> for a schedule that belongs to none.</param>
    /// <param name="id">The identity of the schedule.</param>
    /// <param name="enabled">The new enabled state.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when that identity named a schedule of that tenant.</returns>
    Task<bool> TrySetEnabledAsync(string? tenantId, long id, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a schedule.
    /// </summary>
    /// <param name="tenantId">The tenant the caller is acting for, or <c>null</c> for a schedule that belongs to none.</param>
    /// <param name="id">The identity of the schedule to remove.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when that identity named a schedule of that tenant and it was removed.</returns>
    Task<bool> TryDeleteAsync(string? tenantId, long id, CancellationToken cancellationToken = default);
}
