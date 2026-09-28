using Bsft.Data.Contracts.Pagination;
using Bsft.Scheduling.Reading;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// The operator read side of the job run table on EF Core.
/// </summary>
/// <remarks>
/// Every query is <c>AsNoTracking</c>, and the table carries no tenant column - it is written inside
/// whichever database the host's own context is bound to - so the database is the scope of anything
/// served from here.
/// <para>
/// Unlike <c>EFCoreJobStore</c> this is scoped and takes the context directly: it answers a request,
/// which already has a scope, rather than a poll loop that must not hold one open.
/// </para>
/// </remarks>
internal sealed class EFCoreJobRunReader : IJobRunReader
{
    /// <summary>The character that makes the next one in a LIKE pattern literal.</summary>
    private const string LikeEscape = "\\";

    private readonly IJobDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="EFCoreJobRunReader"/> class.</summary>
    /// <param name="db">The context holding the runs.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
    public EFCoreJobRunReader(IJobDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public async Task<PagedResult<JobRunSummary>> ListAsync(
        JobRunFilter? filter,
        JobRunOrder order,
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A page below one and a size below one are both read as the first page rather than refused:
        // this is a listing, and an operator who mistypes a page number wants the first page, not a
        // stack trace.
        var page = Math.Max(1, request.Page);
        var size = Math.Max(1, request.PageSize);
        var skipped = (page - 1) * size;

        var query = Order(Narrow(_db.JobRuns.AsNoTracking(), filter), order);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.Skip(skipped).Take(size).ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<JobRunSummary>(
            rows.ConvertAll(Summarize), total, page, size, skipped + rows.Count < total);
    }

    /// <inheritdoc />
    public async Task<JobRunView?> FindAsync(long runId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.JobRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(run => run.Id == runId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : new JobRunView(Summarize(entity), entity.Payload);
    }

    /// <summary>Applies whichever criteria the caller set.</summary>
    /// <param name="query">The query to narrow.</param>
    /// <param name="filter">The criteria, or null for none.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<JobRunEntity> Narrow(IQueryable<JobRunEntity> query, JobRunFilter? filter)
    {
        if (filter is null)
            return query;

        return NarrowByWindow(NarrowByIdentity(query, filter), filter);
    }

    /// <summary>Narrows to the job, the state and the instance the caller named.</summary>
    /// <param name="query">The query to narrow.</param>
    /// <param name="filter">The criteria.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<JobRunEntity> NarrowByIdentity(
        IQueryable<JobRunEntity> query, JobRunFilter filter)
    {
        if (filter.JobName is { } jobName)
            query = query.Where(run => run.JobName == jobName);

        if (!string.IsNullOrEmpty(filter.JobNamePrefix))
        {
            // LIKE rather than StartsWith, because this runs in the database: the overload that takes
            // a StringComparison has no translation, and the one that does not is a culture-sensitive
            // comparison as far as anything reading this file can tell. The pattern is escaped and the
            // escape character declared, so a prefix carrying a _ or a % matches itself.
            var pattern = LikePrefix(filter.JobNamePrefix);
            query = query.Where(run => EF.Functions.Like(run.JobName, pattern, LikeEscape));
        }

        if (filter.Status is { } status)
            query = query.Where(run => run.Status == status);

        if (filter.IsRecurring is { } recurring)
            query = query.Where(run => run.IsRecurring == recurring);

        if (filter.CorrelationId is { } correlationId)
            query = query.Where(run => run.CorrelationId == correlationId);

        if (filter.LeaseOwner is { } owner)
            query = query.Where(run => run.LeaseOwner == owner);

        if (filter.MinAttemptCount is { } attempts)
            query = query.Where(run => run.AttemptCount >= attempts);

        return query;
    }

    /// <summary>Narrows to when the run was due and when it settled.</summary>
    /// <param name="query">The query to narrow.</param>
    /// <param name="filter">The criteria.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<JobRunEntity> NarrowByWindow(IQueryable<JobRunEntity> query, JobRunFilter filter)
    {
        if (filter.DueAfter is { } dueAfter)
        {
            var instant = dueAfter.UtcDateTime;
            query = query.Where(run => run.DueAt >= instant);
        }

        if (filter.DueBefore is { } dueBefore)
        {
            var instant = dueBefore.UtcDateTime;
            query = query.Where(run => run.DueAt < instant);
        }

        // A run that has not settled is excluded from a settlement window rather than counted as
        // settled at the beginning of time: "what failed last night" is a question about runs that
        // finished, and one still running has not.
        if (filter.SettledAfter is { } settledAfter)
        {
            var instant = settledAfter.UtcDateTime;
            query = query.Where(run => run.SettledAt != null && run.SettledAt >= instant);
        }

        if (filter.SettledBefore is { } settledBefore)
        {
            var instant = settledBefore.UtcDateTime;
            query = query.Where(run => run.SettledAt != null && run.SettledAt < instant);
        }

        return query;
    }

    /// <summary>Orders the query, ending in the identity so a page boundary is stable.</summary>
    /// <param name="query">The query to order.</param>
    /// <param name="order">The order asked for.</param>
    /// <returns>The ordered query.</returns>
    private static IQueryable<JobRunEntity> Order(IQueryable<JobRunEntity> query, JobRunOrder order) =>
        order switch
        {
            JobRunOrder.DueAscending => query.OrderBy(run => run.DueAt).ThenBy(run => run.Id),
            JobRunOrder.SettledDescending => query
                .OrderBy(run => run.SettledAt == null)   // nulls last, expressed portably
                .ThenByDescending(run => run.SettledAt)
                .ThenBy(run => run.Id),
            JobRunOrder.SettledAscending => query
                .OrderBy(run => run.SettledAt == null)
                .ThenBy(run => run.SettledAt)
                .ThenBy(run => run.Id),
            JobRunOrder.LeaseExpiry => query
                .OrderBy(run => run.LeaseExpiresAt == null)
                .ThenBy(run => run.LeaseExpiresAt)
                .ThenBy(run => run.Id),
            _ => query.OrderByDescending(run => run.DueAt).ThenBy(run => run.Id),
        };

    /// <summary>Reads one row as the listing shows it.</summary>
    /// <param name="entity">The row.</param>
    /// <returns>The summary.</returns>
    private static JobRunSummary Summarize(JobRunEntity entity) => new(
        entity.Id,
        entity.JobName,
        Moment(entity.DueAt),
        entity.Status,
        entity.IsRecurring,
        entity.CorrelationId,
        entity.AttemptCount,
        entity.LeaseOwner,
        Moment(entity.LeaseExpiresAt),
        Moment(entity.SettledAt));

    /// <summary>Reads a stored instant as an offset in UTC.</summary>
    /// <param name="value">The stored instant.</param>
    /// <returns>The same instant, in UTC.</returns>
    private static DateTimeOffset Moment(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>Reads a stored instant as an offset in UTC, carrying absence through.</summary>
    /// <param name="value">The stored instant, or null.</param>
    /// <returns>The same instant in UTC, or null.</returns>
    private static DateTimeOffset? Moment(DateTime? value) => value is null ? null : Moment(value.Value);

    /// <summary>Escapes a prefix and turns it into a LIKE pattern that matches it literally.</summary>
    /// <param name="prefix">The prefix as the caller wrote it.</param>
    /// <returns>The pattern.</returns>
    private static string LikePrefix(string prefix)
    {
        var escaped = prefix
            .Replace(LikeEscape, $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
            .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
            .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal);

        return $"{escaped}%";
    }
}
