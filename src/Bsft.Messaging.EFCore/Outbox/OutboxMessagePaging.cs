using Bsft.Data.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// Turns an ordered query and a page request into a page.
/// </summary>
/// <remarks>
/// Shared by the two readers below rather than written twice: they page the same way, and a second
/// copy is a second place for the off-by-one in <c>HasNextPage</c> to be got wrong.
/// </remarks>
internal static class OutboxMessagePaging
{
    /// <summary>Reads one page of an ordered query, projecting each row as it goes.</summary>
    /// <typeparam name="TEntity">The row type.</typeparam>
    /// <typeparam name="TView">The projected type.</typeparam>
    /// <param name="query">The filtered, ordered query.</param>
    /// <param name="project">How a row becomes a view.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The page.</returns>
    internal static async Task<PagedResult<TView>> PageAsync<TEntity, TView>(
        IQueryable<TEntity> query,
        Func<TEntity, TView> project,
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        // A page below one and a size below one are both read as the first page of one row rather
        // than refused: this is a listing, and an operator who mistypes a page number wants the
        // first page, not a stack trace.
        var page = Math.Max(1, request.Page);
        var size = Math.Max(1, request.PageSize);
        var skipped = (page - 1) * size;

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.Skip(skipped).Take(size).ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<TView>(rows.ConvertAll(row => project(row)), total, page, size, skipped + rows.Count < total);
    }
}
