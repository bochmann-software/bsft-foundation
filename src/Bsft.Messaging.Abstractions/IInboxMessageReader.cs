using Bsft.Data.Contracts.Pagination;

namespace Bsft.Messaging;

/// <summary>
/// The read side of the inbox deduplication ledger: what a consumer has already handled.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="IOutboxMessageReader"/> and deliberately smaller, because the table
/// is. It answers the question that follows a support call - "was this event ever delivered, and to
/// whom" - which otherwise needs a database session.
/// </remarks>
public interface IInboxMessageReader
{
    /// <summary>Reads one page of the ledger, newest first.</summary>
    /// <param name="filter">The criteria rows must match, or <see langword="null"/> for all of them.</param>
    /// <param name="request">The page to read. The request's sort is not honoured.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<InboxMessageSummary>> ListAsync(
        InboxMessageFilter? filter, PagedRequest request, CancellationToken cancellationToken = default);
}
