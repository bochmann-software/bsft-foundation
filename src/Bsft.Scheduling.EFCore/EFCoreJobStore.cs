using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// An <see cref="IJobStore"/> backed by EF Core. It is a singleton that resolves a scoped
/// <see cref="IJobDbContext"/> per operation, so it never holds a context across a poll interval.
/// </summary>
internal sealed class EFCoreJobStore : IJobStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobRunMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreJobStore"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory used to resolve the scoped DbContext.</param>
    /// <param name="mapper">Translates between the scheduler's runs and the persisted rows.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public EFCoreJobStore(IServiceScopeFactory scopeFactory, IJobRunMapper mapper)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc />
    public async Task<long> EnqueueAsync(JobRun run, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        var entity = _mapper.ToEntity(run);
        entity.ConcurrencyToken = Guid.NewGuid();

        db.JobRuns.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Stamp the caller's instance so it can cancel the run by id without a round trip.
        run.Id = entity.Id;
        run.ConcurrencyToken = entity.ConcurrencyToken;
        return entity.Id;
    }

    /// <inheritdoc />
    public async Task<bool> TryEnqueueRecurringAsync(JobRun run, JobOverlapPolicy overlapPolicy, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        var jobName = run.JobName;
        var isRunningLive = overlapPolicy == JobOverlapPolicy.Forbid;

        // The read is the cheap path; the unique filtered index on pending rows is the correctness
        // path. Two instances seeding at the same moment can both pass this check, and the second
        // one's insert is rejected below rather than producing a duplicate occurrence.
        if (await HasLiveOccurrenceAsync(db, jobName, isRunningLive, cancellationToken).ConfigureAwait(false))
            return false;

        var entity = _mapper.ToEntity(run);
        entity.ConcurrencyToken = Guid.NewGuid();
        db.JobRuns.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A rejected insert is only benign when it was the unique occurrence index refusing a
            // duplicate, which means another instance created the occurrence between the check and
            // here. A save can fail for reasons that are nothing of the sort — a name past the
            // column length, a schema that does not match the model — and reporting those as "it is
            // there already" would leave the recurring job with no occurrence, no exception and
            // nothing in the log. Only an occurrence that is actually there proves the race.
            if (await HasLiveOccurrenceAsync(db, jobName, isRunningLive, cancellationToken).ConfigureAwait(false))
                return false;

            throw;
        }

        run.Id = entity.Id;
        run.ConcurrencyToken = entity.ConcurrencyToken;
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRun>> ListClaimableAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        var nowUtc = now.UtcDateTime;

        var entities = await db.JobRuns
            .AsNoTracking()
            .Where(e => (e.Status == JobRunStatus.Pending && e.DueAt <= nowUtc)
                || (e.Status == JobRunStatus.Running && e.LeaseExpiresAt != null && e.LeaseExpiresAt <= nowUtc))
            .OrderBy(e => e.DueAt)
            .ThenBy(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.ConvertAll(_mapper.ToRun);
    }

    /// <inheritdoc />
    public async Task<JobRun?> ClaimAsync(
        JobRun run, string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));
        if (owner is null)
            throw new ArgumentNullException(nameof(owner));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        var nowUtc = now.UtcDateTime;
        var leaseUntil = leaseExpiresAt.UtcDateTime;
        var claimedToken = Guid.NewGuid();
        var id = run.Id;
        var token = run.ConcurrencyToken;

        // One conditional statement rather than a tracked save. The token in the predicate is what
        // makes the claim atomic: an affected count of zero means another instance got there first,
        // or the run was cancelled between the poll and now.
        var claimed = await db.JobRuns
            .Where(e => e.Id == id
                && e.ConcurrencyToken == token
                && ((e.Status == JobRunStatus.Pending && e.DueAt <= nowUtc)
                    || (e.Status == JobRunStatus.Running && e.LeaseExpiresAt != null && e.LeaseExpiresAt <= nowUtc)))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Status, JobRunStatus.Running)
                    .SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1)
                    .SetProperty(e => e.LeaseOwner, owner)
                    .SetProperty(e => e.LeaseExpiresAt, (DateTime?)leaseUntil)
                    .SetProperty(e => e.ConcurrencyToken, claimedToken),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed != 1)
            return null;

        // Returning a projection of what was just written avoids a second round trip; every field
        // the update touched is known here.
        var result = run.Copy();
        result.Status = JobRunStatus.Running;
        result.AttemptCount = run.AttemptCount + 1;
        result.LeaseOwner = owner;
        result.LeaseExpiresAt = leaseExpiresAt;
        result.ConcurrencyToken = claimedToken;
        return result;
    }

    /// <inheritdoc />
    public async Task SettleAsync(JobRun run, JobRunStatus status, DateTimeOffset settledAt, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        var settledAtUtc = settledAt.UtcDateTime;
        var nextToken = Guid.NewGuid();

        // The SetProperty chain is written out here rather than shared with RetryAsync's: ExecuteUpdate
        // parses the argument as an expression tree, and a delegate invoked inside it is not something
        // the provider can translate, so the two chains cannot be composed from a common part.
        await HeldBy(db, run)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Status, status)
                    .SetProperty(e => e.SettledAt, (DateTime?)settledAtUtc)
                    .SetProperty(e => e.LeaseOwner, (string?)null)
                    .SetProperty(e => e.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(e => e.ConcurrencyToken, nextToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RetryAsync(JobRun run, DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        var dueAtUtc = dueAt.UtcDateTime;
        var nextToken = Guid.NewGuid();
        var keepsSlot = run.IsRecurring && !await HasSuccessorAsync(db, run, cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteRetryAsync(db, run, dueAtUtc, keepsSlot, nextToken, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException) when (keepsSlot)
        {
            // The successor check and this update are two statements, and the slot can be taken between
            // them: the run is Running, which the unique pending-occurrence index does not cover, so the
            // periodic re-seed on another instance sees no pending occurrence and creates one. Claiming
            // the slot then violates the index. Letting that out would abort the dispatch pass and leave
            // the row Running with an attempt charged until its lease lapsed, so the retry is re-issued
            // giving the slot up — the occurrence that won the race carries the schedule from here on.
            //
            // A DbException rather than a DbUpdateException: ExecuteUpdate is a statement of its own and
            // does not run through the SaveChanges pipeline that would wrap it. Nothing else about the
            // update can violate a constraint, and a failure that was never the collision — an
            // unreachable database, a schema that does not match — fails the second statement too and
            // reaches the caller from there rather than being swallowed here.
            await WriteRetryAsync(db, run, dueAtUtc, keepsSlot: false, nextToken, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the run this caller still holds to pending, optionally giving up the recurring job's
    /// single pending-occurrence slot.
    /// </summary>
    private static Task<int> WriteRetryAsync(
        IJobDbContext db, JobRun run, DateTime dueAtUtc, bool keepsSlot, Guid nextToken, CancellationToken cancellationToken) =>
        HeldBy(db, run)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Status, JobRunStatus.Pending)
                    .SetProperty(e => e.DueAt, dueAtUtc)
                    .SetProperty(e => e.IsRecurring, keepsSlot)
                    .SetProperty(e => e.LeaseOwner, (string?)null)
                    .SetProperty(e => e.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(e => e.ConcurrencyToken, nextToken),
                cancellationToken);

    /// <inheritdoc />
    public async Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        // Only a run that has not started may be cancelled; deleting a running one would strand the
        // instance that holds its lease.
        var deleted = await db.JobRuns
            .Where(e => e.Id == id && e.Status == JobRunStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted == 1;
    }

    /// <inheritdoc />
    public async Task<JobRun?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        var entity = await db.JobRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : _mapper.ToRun(entity);
    }

    /// <summary>
    /// Determines whether the recurring job already occupies its single-occurrence slot: a pending
    /// occurrence always does, a running one only under <see cref="JobOverlapPolicy.Forbid"/>.
    /// </summary>
    private static Task<bool> HasLiveOccurrenceAsync(
        IJobDbContext db, string jobName, bool isRunningLive, CancellationToken cancellationToken) =>
        db.JobRuns
            .AsNoTracking()
            .AnyAsync(
                e => e.JobName == jobName
                    && e.IsRecurring
                    && (e.Status == JobRunStatus.Pending
                        || (isRunningLive && e.Status == JobRunStatus.Running)),
                cancellationToken);

    /// <summary>
    /// Determines whether the recurring job's single pending-occurrence slot is already held by a run
    /// other than this one — its successor, created before the work started under
    /// <see cref="JobOverlapPolicy.Allow"/>.
    /// </summary>
    private static Task<bool> HasSuccessorAsync(IJobDbContext db, JobRun run, CancellationToken cancellationToken)
    {
        var jobName = run.JobName;
        var id = run.Id;

        return db.JobRuns
            .AsNoTracking()
            .AnyAsync(
                e => e.Id != id
                    && e.JobName == jobName
                    && e.IsRecurring
                    && e.Status == JobRunStatus.Pending,
                cancellationToken);
    }

    /// <summary>
    /// Narrows the set to the single run whose lease the caller holds, identified by its concurrency
    /// token. A token that no longer matches means the lease was lost to another instance, and this
    /// caller's outcome must not overwrite the winner's.
    /// </summary>
    private static IQueryable<JobRunEntity> HeldBy(IJobDbContext db, JobRun run)
    {
        var id = run.Id;
        var token = run.ConcurrencyToken;

        return db.JobRuns.Where(e => e.Id == id && e.ConcurrencyToken == token);
    }
}
