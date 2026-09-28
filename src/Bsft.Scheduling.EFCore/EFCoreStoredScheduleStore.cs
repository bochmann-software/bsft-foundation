using Bsft.Scheduling.StoredSchedules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// An <see cref="IStoredScheduleStore"/> backed by EF Core. It is a singleton that resolves a scoped
/// <see cref="IStoredScheduleDbContext"/> per operation, so it never holds a context across a poll
/// interval — the same shape <see cref="Bsft.Scheduling.EFCore.EFCoreJobStore"/> uses.
/// </summary>
internal sealed class EFCoreStoredScheduleStore : IStoredScheduleStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStoredScheduleMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreStoredScheduleStore"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory used to resolve the scoped DbContext.</param>
    /// <param name="mapper">Translates between stored schedules and the persisted rows.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public EFCoreStoredScheduleStore(IServiceScopeFactory scopeFactory, IStoredScheduleMapper mapper)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc />
    public async Task<long> CreateAsync(StoredSchedule schedule, CancellationToken cancellationToken = default)
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>();

        var entity = _mapper.ToEntity(schedule);
        entity.ConcurrencyToken = Guid.NewGuid();

        db.StoredSchedules.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        schedule.Id = entity.Id;
        schedule.ConcurrencyToken = entity.ConcurrencyToken;
        return entity.Id;
    }

    /// <inheritdoc />
    public async Task<StoredSchedule?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>();

        var entity = await db.StoredSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : _mapper.ToSchedule(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredSchedule>> ListByTenantAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>();

        var entities = await db.StoredSchedules
            .AsNoTracking()
            .Where(e => tenantId == null || e.TenantId == tenantId)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.ConvertAll(_mapper.ToSchedule);
    }

    /// <inheritdoc />
    public async Task<bool> TryUpdateAsync(StoredSchedule schedule, CancellationToken cancellationToken = default)
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>();

        var updated = _mapper.ToEntity(schedule);
        var nextToken = Guid.NewGuid();

        // The token is precomputed into a local rather than called inline: ExecuteUpdate parses the
        // argument as an expression tree, and Guid.NewGuid() is not something the provider can
        // translate as one of its arms.
        var affected = await db.StoredSchedules
            .Where(e => e.Id == schedule.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.JobKey, updated.JobKey)
                    .SetProperty(e => e.CronExpression, updated.CronExpression)
                    .SetProperty(e => e.IntervalTicks, updated.IntervalTicks)
                    .SetProperty(e => e.Payload, updated.Payload)
                    .SetProperty(e => e.NextRunAt, updated.NextRunAt)
                    .SetProperty(e => e.UpdatedAt, updated.UpdatedAt)
                    .SetProperty(e => e.ConcurrencyToken, nextToken),
                cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TrySetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>();

        var nextToken = Guid.NewGuid();

        var affected = await db.StoredSchedules
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Enabled, enabled)
                    .SetProperty(e => e.ConcurrencyToken, nextToken),
                cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TryDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>();

        var deleted = await db.StoredSchedules
            .Where(e => e.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredSchedule>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>();

        var nowUtc = now.UtcDateTime;

        var entities = await db.StoredSchedules
            .AsNoTracking()
            .Where(e => e.Enabled && e.NextRunAt != null && e.NextRunAt <= nowUtc)
            .OrderBy(e => e.NextRunAt)
            .ThenBy(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.ConvertAll(_mapper.ToSchedule);
    }

    /// <inheritdoc />
    public async Task<StoredSchedule?> AdvanceOrNullAsync(
        StoredSchedule schedule, DateTimeOffset ranAt, DateTimeOffset? nextRunAt, CancellationToken cancellationToken = default)
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>();

        var ranAtUtc = ranAt.UtcDateTime;
        var nextRunAtUtc = nextRunAt?.UtcDateTime;
        var advancedToken = Guid.NewGuid();
        var id = schedule.Id;
        var token = schedule.ConcurrencyToken;

        // One conditional statement rather than a tracked save. The token in the predicate is what
        // makes the advance atomic: an affected count of zero means another instance already claimed
        // this due slot, or the schedule was deleted between the read and here.
        var advanced = await db.StoredSchedules
            .Where(e => e.Id == id && e.ConcurrencyToken == token)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.LastRunAt, ranAtUtc)
                    .SetProperty(e => e.NextRunAt, nextRunAtUtc)
                    .SetProperty(e => e.UpdatedAt, ranAtUtc)
                    .SetProperty(e => e.ConcurrencyToken, advancedToken),
                cancellationToken)
            .ConfigureAwait(false);

        if (advanced != 1)
            return null;

        var result = schedule.Copy();
        result.LastRunAt = ranAt;
        result.NextRunAt = nextRunAt;
        result.UpdatedAt = ranAt;
        result.ConcurrencyToken = advancedToken;
        return result;
    }
}
