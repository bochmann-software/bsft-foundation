using Bsft.Data.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// The outbox read side on EF Core.
/// </summary>
/// <remarks>
/// Every query is <c>AsNoTracking</c> and ordered newest first. The outbox table carries no tenant
/// column - it is written by the interceptor inside whichever database the host's own
/// <c>DbContext</c> is bound to, so the database is its scope - which is worth knowing before this
/// surface is exposed to anyone who may not see the whole installation's traffic.
/// </remarks>
internal sealed class EFCoreOutboxMessageReader : IOutboxMessageReader
{
    /// <summary>The character that makes the next one in a LIKE pattern literal.</summary>
    private const string LikeEscape = "\\";

    private readonly IOutboxDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="EFCoreOutboxMessageReader"/> class.</summary>
    /// <param name="db">The context holding the outbox table.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is <c>null</c>.</exception>
    public EFCoreOutboxMessageReader(IOutboxDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <c>null</c>.</exception>
    public Task<PagedResult<OutboxMessageSummary>> ListAsync(
        OutboxMessageFilter? filter, PagedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = Narrow(_db.OutboxMessages.AsNoTracking(), filter)
            .OrderByDescending(entity => entity.Id);

        return OutboxMessagePaging.PageAsync(query, Summarize, request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OutboxMessageView?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.OutboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : new OutboxMessageView(Summarize(entity), entity.PayloadJson);
    }

    /// <summary>Applies the filter, where one was given.</summary>
    /// <param name="query">The unfiltered query.</param>
    /// <param name="filter">The criteria, or null.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<OutboxMessageEntity> Narrow(
        IQueryable<OutboxMessageEntity> query, OutboxMessageFilter? filter)
    {
        if (filter is null)
            return query;

        query = NarrowByState(query, filter.State);
        query = NarrowByEvent(query, filter);
        return NarrowByWindow(query, filter);
    }

    /// <summary>Applies the state, which is read from the timestamps rather than a column.</summary>
    /// <param name="query">The query so far.</param>
    /// <param name="state">The state asked for, or null.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<OutboxMessageEntity> NarrowByState(
        IQueryable<OutboxMessageEntity> query, OutboxMessageState? state) => state switch
    {
        OutboxMessageState.Pending => query.Where(e => e.ProcessedAt == null && e.DeadLetteredAt == null),
        OutboxMessageState.Dispatched => query.Where(e => e.ProcessedAt != null),
        OutboxMessageState.DeadLettered => query.Where(e => e.DeadLetteredAt != null),
        _ => query
    };

    /// <summary>Applies the identity criteria.</summary>
    /// <param name="query">The query so far.</param>
    /// <param name="filter">The criteria.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<OutboxMessageEntity> NarrowByEvent(
        IQueryable<OutboxMessageEntity> query, OutboxMessageFilter filter)
    {
        if (filter.EventTypeName is { } typeName)
            query = query.Where(e => e.EventTypeName == typeName);

        if (!string.IsNullOrEmpty(filter.EventTypeNamePrefix))
        {
            // LIKE rather than StartsWith, because this runs in the database: the overload that takes
            // a StringComparison has no translation, and the one that does not is a culture-sensitive
            // comparison as far as anything reading this file can tell. The pattern is escaped and the
            // escape character declared, so a prefix carrying a _ or a % matches itself.
            var pattern = LikePrefix(filter.EventTypeNamePrefix);
            query = query.Where(e => EF.Functions.Like(e.EventTypeName, pattern, LikeEscape));
        }

        if (filter.EventId is { } eventId)
            query = query.Where(e => e.EventId == eventId);

        if (filter.CorrelationId is { } correlationId)
            query = query.Where(e => e.CorrelationId == correlationId);

        return query;
    }

    /// <summary>Applies the time window and the attempt floor.</summary>
    /// <param name="query">The query so far.</param>
    /// <param name="filter">The criteria.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<OutboxMessageEntity> NarrowByWindow(
        IQueryable<OutboxMessageEntity> query, OutboxMessageFilter filter)
    {
        if (filter.OccurredAfter is { } after)
        {
            var from = after.UtcDateTime;
            query = query.Where(e => e.OccurredAt >= from);
        }

        if (filter.OccurredBefore is { } before)
        {
            var to = before.UtcDateTime;
            query = query.Where(e => e.OccurredAt <= to);
        }

        if (filter.MinAttemptCount is { } attempts)
            query = query.Where(e => e.AttemptCount >= attempts);

        return query;
    }

    /// <summary>Reads a row as a listing row, leaving the payload behind.</summary>
    /// <param name="entity">The row.</param>
    /// <returns>The summary.</returns>
    private static OutboxMessageSummary Summarize(OutboxMessageEntity entity) => new(
        entity.Id,
        entity.EventId,
        entity.EventTypeName,
        Moment(entity.OccurredAt),
        entity.CorrelationId,
        ReadState(entity),
        entity.AttemptCount,
        Moment(entity.NextAttemptAt),
        Moment(entity.LeasedUntil),
        Moment(entity.ProcessedAt),
        Moment(entity.DeadLetteredAt),
        entity.FailureReason);

    /// <summary>Reads a row's state from its timestamps.</summary>
    /// <param name="entity">The row.</param>
    /// <returns>The state.</returns>
    /// <remarks>
    /// Retirement is asked first. A row can carry both timestamps only if something published it
    /// after giving up on it, and in that case the honest answer to an operator is the one that says
    /// a human was needed.
    /// </remarks>
    private static OutboxMessageState ReadState(OutboxMessageEntity entity) => entity switch
    {
        { DeadLetteredAt: not null } => OutboxMessageState.DeadLettered,
        { ProcessedAt: not null } => OutboxMessageState.Dispatched,
        _ => OutboxMessageState.Pending
    };

    /// <summary>Reads a stored UTC timestamp as an instant.</summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The instant.</returns>
    private static DateTimeOffset Moment(DateTime value) => new(value, TimeSpan.Zero);

    /// <summary>Reads a stored UTC timestamp as an instant, where there is one.</summary>
    /// <param name="value">The stored value, or null.</param>
    /// <returns>The instant, or null.</returns>
    private static DateTimeOffset? Moment(DateTime? value) => value is { } moment ? Moment(moment) : null;

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
