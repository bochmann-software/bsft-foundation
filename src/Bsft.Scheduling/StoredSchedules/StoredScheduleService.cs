namespace Bsft.Scheduling.StoredSchedules;

/// <summary>
/// Default <see cref="IStoredScheduleService"/>.
/// </summary>
internal sealed class StoredScheduleService : IStoredScheduleService
{
    private readonly IStoredScheduleStore _store;
    private readonly IJobRegistry _registry;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredScheduleService"/> class.
    /// </summary>
    /// <param name="store">The store schedules are written to and read from.</param>
    /// <param name="registry">The registry a job key is validated against.</param>
    /// <param name="timeProvider">Supplies the instant a schedule's next occurrence is anchored to.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public StoredScheduleService(IStoredScheduleStore store, IJobRegistry registry, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<StoredSchedule> CreateAsync(
        string? tenantId, string jobKey, JobSchedule schedule, string? payload, CancellationToken cancellationToken = default)
    {
        if (jobKey is null)
            throw new ArgumentNullException(nameof(jobKey));
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        // Rejecting an unknown job key here rather than at dispatch time keeps a typo from becoming a
        // schedule that sits in the catalog forever and is only noticed when nothing ever runs.
        if (_registry.Find(jobKey) is null)
            throw JobNotRegisteredException.ForName(jobKey);

        var now = _timeProvider.GetUtcNow();

        var toCreate = new StoredSchedule
        {
            TenantId = tenantId,
            JobKey = jobKey,
            Schedule = schedule,
            Payload = payload,
            Enabled = true,
            NextRunAt = schedule.FindNextOccurrence(now),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _store.CreateAsync(toCreate, cancellationToken).ConfigureAwait(false);
        return toCreate;
    }

    /// <inheritdoc />
    public Task<StoredSchedule?> FindAsync(string? tenantId, long id, CancellationToken cancellationToken = default) =>
        OwnedOrNullAsync(tenantId, id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredSchedule>> ListAsync(string? tenantId, CancellationToken cancellationToken = default) =>
        _store.ListByTenantAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryUpdateAsync(
        string? tenantId, long id, JobSchedule schedule, string? payload, CancellationToken cancellationToken = default)
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        var existing = await OwnedOrNullAsync(tenantId, id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return false;

        var now = _timeProvider.GetUtcNow();

        // The next occurrence is recomputed from now rather than kept, so an edited schedule follows
        // its new rule from here on instead of firing once more on whatever the old rule had queued.
        existing.Schedule = schedule;
        existing.Payload = payload;
        existing.NextRunAt = schedule.FindNextOccurrence(now);
        existing.UpdatedAt = now;

        return await _store.TryUpdateAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TrySetEnabledAsync(
        string? tenantId, long id, bool enabled, CancellationToken cancellationToken = default)
    {
        if (await OwnedOrNullAsync(tenantId, id, cancellationToken).ConfigureAwait(false) is null)
            return false;

        return await _store.TrySetEnabledAsync(id, enabled, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryDeleteAsync(string? tenantId, long id, CancellationToken cancellationToken = default)
    {
        if (await OwnedOrNullAsync(tenantId, id, cancellationToken).ConfigureAwait(false) is null)
            return false;

        return await _store.TryDeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a schedule only when it belongs to the tenant the caller is acting for.</summary>
    /// <param name="tenantId">The tenant the caller is acting for.</param>
    /// <param name="id">The identity to read.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The schedule, or <c>null</c> when it is missing or somebody else's.</returns>
    /// <remarks>
    /// A schedule's tenant never changes, so reading it and then acting on the identity cannot act on
    /// a row that has since become somebody else's. What can happen between the two is a delete, and
    /// the store then answers false, which is what a caller asking to remove an absent schedule wanted
    /// anyway. Both answers are the same for a missing schedule and for one owned elsewhere, so a
    /// caller counting through identities learns nothing about what other tenants keep.
    /// </remarks>
    private async Task<StoredSchedule?> OwnedOrNullAsync(string? tenantId, long id, CancellationToken cancellationToken)
    {
        var stored = await _store.FindAsync(id, cancellationToken).ConfigureAwait(false);

        return string.Equals(stored?.TenantId, tenantId, StringComparison.Ordinal) ? stored : null;
    }
}
