using Bsft.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// The retention pass over the job-run table: it deletes the settled runs that settled before the cutoff.
/// </summary>
/// <remarks>
/// <para>
/// Only the delete lives here. The loop, the interval, the retention window, the batch pacing, the
/// startup offset and the failure handling belong to <c>AddBsftRetention</c> and are shared with every
/// other retention pass in the framework — see <c>AddBsftJobRunEFCoreRetention</c>, which maps
/// <see cref="BsftSchedulingOptions.JobRunRetention"/> onto that sweeper's options.
/// </para>
/// <para>
/// A settled run is updated in place and never deleted by the store, so a scheduler with a few jobs on a
/// ten second cadence adds thousands of rows a day to the table its dispatcher polls. Runs are aged by the
/// instant they settled, not the instant they were due. Only <see cref="JobRunStatus.Completed"/>,
/// <see cref="JobRunStatus.Failed"/> and <see cref="JobRunStatus.Skipped"/> runs are candidates: a pending
/// run is a job's single occurrence slot and a running one is the row a lease is written on, so neither is
/// ever touched, however old.
/// </para>
/// <para>
/// With <see cref="BsftSchedulingOptions.JobRunRetentionKeepLatest"/> above zero, the latest settled runs
/// of each job (by settle instant, then identity) are spared whatever their age, so a job that runs rarely
/// still shows its last outcomes. The sweep reads the jobs that have something expired, then one boundary
/// per job (the Nth newest settled run) and deletes what lies before both the boundary and the cutoff.
/// </para>
/// <para>
/// Deliberately no index of its own, because the package ships no migration. With keep-latest off, the sweep
/// is paced and rare, while an index on the settle instant would be written on every settle. With it on,
/// each job's boundary is a descending read of that job's settled runs, which an index on
/// <c>(JobName, SettledAt, Id)</c> turns into a seek; a host with millions of settled runs adds it in its
/// own migration (see docs/scheduling.md).
/// </para>
/// </remarks>
public sealed class JobRunRetentionPass : IRetentionPass
{
    // The most jobs one batch looks at. The distinct jobs of a table are as many as the host registers,
    // so this is a guard on a read that is small in practice, not a limit anyone is expected to reach.
    private const int MaxJobsPerBatch = 10_000;

    private readonly IJobDbContext _db;
    private readonly IOptions<BsftSchedulingOptions> _options;
    private readonly int _maxJobsPerBatch;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunRetentionPass"/> class.
    /// </summary>
    /// <param name="db">The context the runs are deleted from.</param>
    /// <param name="options">The options that say how many of each job's latest settled runs are kept.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public JobRunRetentionPass(IJobDbContext db, IOptions<BsftSchedulingOptions> options)
        : this(db, options, MaxJobsPerBatch)
    {
    }

    internal JobRunRetentionPass(IJobDbContext db, IOptions<BsftSchedulingOptions> options, int maxJobsPerBatch)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _maxJobsPerBatch = maxJobsPerBatch;
    }

    /// <inheritdoc />
    public async Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = olderThan.UtcDateTime;
        var keepLatest = _options.Value.JobRunRetentionKeepLatest;

        return keepLatest <= 0
            ? await DeleteOldestAsync(Expired(cutoff), batchSize, cancellationToken).ConfigureAwait(false)
            : await PurgeBeyondLatestAsync(cutoff, keepLatest, batchSize, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> PurgeBeyondLatestAsync(DateTime cutoff, int keepLatest, int batchSize, CancellationToken cancellationToken)
    {
        // One boundary per job instead of a correlated count per candidate row: the count is evaluated
        // once for every expired row, which on a table of millions of settled runs never finishes. Only
        // jobs with something deletable are listed: more settled runs than are kept and the oldest of
        // them expired, which also means the oldest is not among the kept ones. A job without that can
        // never lose a row and must not take a place in the capped list ahead of one with a backlog.
        // The job holding the oldest run comes first, so a sweep that ends on the batch cap has taken
        // the oldest of what there is.
        var jobs = await _db.JobRuns
            .Where(e => e.Status >= JobRunStatus.Completed && e.SettledAt != null)
            .GroupBy(e => e.JobName)
            .Where(g => g.Count() > keepLatest && g.Min(e => e.SettledAt) < cutoff)
            .Select(g => new { JobName = g.Key, Oldest = g.Min(e => e.SettledAt) })
            .OrderBy(g => g.Oldest)
            .ThenBy(g => g.JobName)
            .Take(_maxJobsPerBatch)
            .Select(g => g.JobName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deleted = 0;

        foreach (var job in jobs)
        {
            if (deleted >= batchSize)
                break;

            deleted += await PurgeJobAsync(job, cutoff, keepLatest, batchSize - deleted, cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private async Task<int> PurgeJobAsync(string job, DateTime cutoff, int keepLatest, int room, CancellationToken cancellationToken)
    {
        // The keepLatest-th newest settled run of the job: it and everything newer is kept. A job with
        // fewer settled runs than that has no boundary and loses nothing.
        var boundary = await _db.JobRuns
            .Where(e => e.JobName == job && e.Status >= JobRunStatus.Completed && e.SettledAt != null)
            .OrderByDescending(e => e.SettledAt)
            .ThenByDescending(e => e.Id)
            .Skip(keepLatest - 1)
            .Select(e => new { e.SettledAt, e.Id })
            .Take(1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Null only when a concurrent sweep removed runs between the listing and this read.
        return boundary is null
            ? 0
            : await DeleteOldestAsync(
                Expired(cutoff).Where(e => e.JobName == job
                    && (e.SettledAt < boundary.SettledAt || (e.SettledAt == boundary.SettledAt && e.Id < boundary.Id))),
                room,
                cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> DeleteOldestAsync(IQueryable<JobRunEntity> expired, int count, CancellationToken cancellationToken)
    {
        // Two statements, not one: EF Core has no portable way to cap a DELETE at N rows, so the
        // bounded, oldest-first page of ids is read and exactly those are deleted. The delete carries
        // the predicate a second time because the two statements are not atomic; nothing revives a
        // settled run, so the worst case is a row a concurrent sweep already deleted.
        var ids = await expired
            .OrderBy(e => e.SettledAt)
            .ThenBy(e => e.Id)
            .Take(count)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ids.Count == 0
            ? 0
            : await expired
                .Where(e => ids.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
    }

    private IQueryable<JobRunEntity> Expired(DateTime cutoff) =>
        _db.JobRuns.Where(e => e.Status >= JobRunStatus.Completed && e.SettledAt != null && e.SettledAt < cutoff);
}
