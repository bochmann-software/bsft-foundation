using Bsft.Data.Contracts.Pagination;

namespace Bsft.Messaging;

/// <summary>
/// The read side of the outbox: what is waiting, what is struggling and what was given up on.
/// </summary>
/// <remarks>
/// The store contracts next door are the dispatcher's: they claim, publish and retire rows, and none
/// of them can answer "show me the messages that need a person". A parked message is by definition
/// one nothing automatic will pick up again, so without this it existed in a table nobody could
/// enumerate. Read-only by design - releasing or replaying a retired message is a write, and belongs
/// to a contract that says so.
/// </remarks>
public interface IOutboxMessageReader
{
    /// <summary>Reads one page of outbox rows, newest first.</summary>
    /// <param name="filter">The criteria rows must match, or <see langword="null"/> for all of them.</param>
    /// <param name="request">The page to read. The request's sort is not honoured; see the remarks.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The page.</returns>
    /// <remarks>
    /// Ordered newest first and the request's sort is ignored, which is the same bargain every other
    /// administrative listing in the framework makes: one useful order, and two implementations that
    /// cannot answer the same request differently.
    /// </remarks>
    Task<PagedResult<OutboxMessageSummary>> ListAsync(
        OutboxMessageFilter? filter, PagedRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads one row with the message it holds.</summary>
    /// <param name="id">The row's identifier, as <see cref="OutboxMessageSummary.Id"/> reports it.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The row, or <see langword="null"/> when there is no such row.</returns>
    Task<OutboxMessageView?> FindAsync(long id, CancellationToken cancellationToken = default);
}
