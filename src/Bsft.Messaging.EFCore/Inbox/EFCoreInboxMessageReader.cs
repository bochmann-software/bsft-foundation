using Bsft.Data.Contracts.Pagination;
using Bsft.Messaging.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Inbox;

/// <summary>
/// The inbox deduplication ledger's read side on EF Core.
/// </summary>
internal sealed class EFCoreInboxMessageReader : IInboxMessageReader
{
    private readonly IInboxDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="EFCoreInboxMessageReader"/> class.</summary>
    /// <param name="db">The context holding the inbox table.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is <c>null</c>.</exception>
    public EFCoreInboxMessageReader(IInboxDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <c>null</c>.</exception>
    public Task<PagedResult<InboxMessageSummary>> ListAsync(
        InboxMessageFilter? filter, PagedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = Narrow(_db.InboxMessages.AsNoTracking(), filter)
            .OrderByDescending(entity => entity.Id);

        return OutboxMessagePaging.PageAsync(query, Summarize, request, cancellationToken);
    }

    /// <summary>Applies the filter, where one was given.</summary>
    /// <param name="query">The unfiltered query.</param>
    /// <param name="filter">The criteria, or null.</param>
    /// <returns>The narrowed query.</returns>
    private static IQueryable<InboxMessageEntity> Narrow(
        IQueryable<InboxMessageEntity> query, InboxMessageFilter? filter)
    {
        if (filter is null)
            return query;

        if (filter.Consumer is { } consumer)
            query = query.Where(e => e.Consumer == consumer);

        if (filter.EventName is { } eventName)
            query = query.Where(e => e.EventName == eventName);

        if (filter.EventId is { } eventId)
            query = query.Where(e => e.EventId == eventId);

        if (filter.ReceivedAfter is { } after)
        {
            var from = after.UtcDateTime;
            query = query.Where(e => e.ReceivedAt >= from);
        }

        if (filter.ReceivedBefore is { } before)
        {
            var to = before.UtcDateTime;
            query = query.Where(e => e.ReceivedAt <= to);
        }

        return query;
    }

    /// <summary>Reads a row as a listing row.</summary>
    /// <param name="entity">The row.</param>
    /// <returns>The summary.</returns>
    private static InboxMessageSummary Summarize(InboxMessageEntity entity) => new(
        entity.Id,
        entity.EventId,
        entity.Consumer,
        entity.EventName,
        new DateTimeOffset(entity.ReceivedAt, TimeSpan.Zero));
}
