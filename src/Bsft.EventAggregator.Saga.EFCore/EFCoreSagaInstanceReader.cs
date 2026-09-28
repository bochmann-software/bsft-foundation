using Bsft.Data.Contracts.Pagination;
using Bsft.EventAggregator.Saga.Reading;
using Microsoft.EntityFrameworkCore;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// The operator read side of the saga state table on EF Core.
/// </summary>
/// <remarks>
/// Every query is <c>AsNoTracking</c>, and the table carries no tenant column - it is written inside
/// whichever database the host's own <c>DbContext</c> is bound to - so the database is the scope of
/// anything served from here.
/// </remarks>
public sealed class EFCoreSagaInstanceReader : ISagaInstanceReader
{
    /// <summary>The character that makes the next one in a LIKE pattern literal.</summary>
    private const string LikeEscape = "\\";

    private readonly ISagaDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="EFCoreSagaInstanceReader"/> class.</summary>
    /// <param name="db">The context holding the saga states.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is <c>null</c>.</exception>
    public EFCoreSagaInstanceReader(ISagaDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <c>null</c>.</exception>
    public async Task<PagedResult<SagaInstanceSummary>> ListAsync(
        SagaInstanceFilter? filter,
        SagaInstanceOrder order,
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

        var query = Order(Narrow(_db.SagaStates.AsNoTracking(), filter), order);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.Skip(skipped).Take(size).ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<SagaInstanceSummary>(
            rows.ConvertAll(Summarize), total, page, size, skipped + rows.Count < total);
    }

    /// <inheritdoc />
    public async Task<SagaInstanceView?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.SagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : new SagaInstanceView(Summarize(entity), entity.StateJson);
    }

    /// <summary>Applies the filter, where one was given.</summary>
    /// <param name="query">The unfiltered query.</param>
    /// <param name="filter">The criteria, or null.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<SagaStateEntity> Narrow(IQueryable<SagaStateEntity> query, SagaInstanceFilter? filter)
    {
        if (filter is null)
            return query;

        query = NarrowByIdentity(query, filter);
        query = NarrowByWindow(query, filter);

        if (filter.IsComplete is { } complete)
            query = query.Where(e => e.IsComplete == complete);

        if (filter.MinStepIndex is { } step)
            query = query.Where(e => e.CurrentStepIndex >= step);

        return query;
    }

    /// <summary>Narrows by what identifies the instance.</summary>
    /// <param name="query">The query so far.</param>
    /// <param name="filter">The criteria.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<SagaStateEntity> NarrowByIdentity(
        IQueryable<SagaStateEntity> query, SagaInstanceFilter filter)
    {
        if (filter.SagaType is { } sagaType)
            query = query.Where(e => e.SagaType == sagaType);

        if (!string.IsNullOrEmpty(filter.SagaTypePrefix))
        {
            // LIKE rather than StartsWith, because this runs in the database: the overload that takes
            // a StringComparison has no translation, and the one that does not is a culture-sensitive
            // comparison as far as anything reading this file can tell. The pattern is escaped and the
            // escape character declared, so a prefix carrying a _ or a % matches itself.
            var pattern = LikePrefix(filter.SagaTypePrefix);
            query = query.Where(e => EF.Functions.Like(e.SagaType, pattern, LikeEscape));
        }

        if (filter.CorrelationKey is { } correlationKey)
            query = query.Where(e => e.CorrelationKey == correlationKey);

        if (filter.StateTypeName is { } stateTypeName)
            query = query.Where(e => e.StateTypeName == stateTypeName);

        return query;
    }

    /// <summary>Narrows by when the instance started and last moved.</summary>
    /// <param name="query">The query so far.</param>
    /// <param name="filter">The criteria.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<SagaStateEntity> NarrowByWindow(
        IQueryable<SagaStateEntity> query, SagaInstanceFilter filter)
    {
        if (filter.CreatedAfter is { } createdAfter)
        {
            var from = createdAfter.UtcDateTime;
            query = query.Where(e => e.CreatedAt >= from);
        }

        if (filter.CreatedBefore is { } createdBefore)
        {
            var to = createdBefore.UtcDateTime;
            query = query.Where(e => e.CreatedAt <= to);
        }

        if (filter.UpdatedAfter is { } updatedAfter)
        {
            var from = updatedAfter.UtcDateTime;
            query = query.Where(e => e.UpdatedAt >= from);
        }

        if (filter.UpdatedBefore is { } updatedBefore)
        {
            var to = updatedBefore.UtcDateTime;
            query = query.Where(e => e.UpdatedAt <= to);
        }

        return query;
    }

    /// <summary>Orders the query the way the caller asked.</summary>
    /// <param name="query">The narrowed query.</param>
    /// <param name="order">The order asked for.</param>
    /// <returns>The ordered query.</returns>
    /// <remarks>
    /// Every order ends in the identity, so a page boundary falls in the same place twice. Without it
    /// two instances that last moved in the same instant could swap between one page and the next.
    /// </remarks>
    private static IQueryable<SagaStateEntity> Order(IQueryable<SagaStateEntity> query, SagaInstanceOrder order) =>
        order switch
        {
            SagaInstanceOrder.UpdatedAscending => query.OrderBy(e => e.UpdatedAt).ThenBy(e => e.Id),
            SagaInstanceOrder.CreatedDescending => query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id),
            SagaInstanceOrder.CreatedAscending => query.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id),
            _ => query.OrderByDescending(e => e.UpdatedAt).ThenBy(e => e.Id)
        };

    /// <summary>Reads a row as the listing shows it.</summary>
    /// <param name="entity">The row.</param>
    /// <returns>The summary.</returns>
    private static SagaInstanceSummary Summarize(SagaStateEntity entity) => new(
        entity.Id,
        entity.SagaType,
        entity.CorrelationKey,
        entity.StateTypeName,
        entity.CurrentStepIndex,
        entity.IsComplete,
        SagaReadInstants.Moment(entity.CreatedAt),
        SagaReadInstants.Moment(entity.UpdatedAt));

    /// <summary>Turns a prefix into a LIKE pattern that matches it literally.</summary>
    /// <param name="prefix">The prefix as it arrived.</param>
    /// <returns>The pattern, with every LIKE metacharacter escaped by <see cref="LikeEscape"/>.</returns>
    private static string LikePrefix(string prefix)
    {
        var escaped = prefix
            .Replace(LikeEscape, $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
            .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
            .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal);

        return $"{escaped}%";
    }
}
