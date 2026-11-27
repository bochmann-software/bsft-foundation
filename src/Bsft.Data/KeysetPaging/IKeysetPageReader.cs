using Bsft.Data.Contracts.Pagination;

namespace Bsft.Data.KeysetPaging;

/// <summary>
/// Reads one page of a keyset listing from an EF Core query: the rows after the cursor's position in the
/// listing's fixed order, and the cursor of the page that follows.
/// </summary>
/// <remarks>
/// The query is narrowed by <c>WHERE (key &gt; @key) OR (key = @key AND id &gt; @id)</c> (with
/// <c>&lt;</c> for a descending order) and ordered by the key, then the id. The values are parameters, the
/// operators are plain LINQ comparisons (a string key through <see cref="string.Compare(string, string)"/>),
/// so every EF Core provider translates it and no SQL is written by hand. One row more than the page size
/// is read to learn whether a next page exists, so no count is needed for that.
/// </remarks>
public interface IKeysetPageReader
{
    /// <summary>Reads the page a request names.</summary>
    /// <typeparam name="TItem">The type of the rows.</typeparam>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <typeparam name="TId">The type of the id.</typeparam>
    /// <param name="query">
    /// The filtered query, without an order of its own: the reader orders it by <paramref name="order"/>.
    /// Must be an EF Core query, because the page is read asynchronously.
    /// </param>
    /// <param name="order">The fixed order of the listing.</param>
    /// <param name="request">
    /// The cursor and the page size; the total only when asked for. A page size above
    /// <see cref="KeysetPagingOptions.MaxPageSize"/> is clamped to it, and the page reports the size applied.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The page, with a next cursor unless it is the last page, and the number of all rows of
    /// <paramref name="query"/> when <see cref="CursorPagedRequest.IncludeTotalCount"/> asks for it.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="NotSupportedException">
    /// Thrown before any query runs when the sort key or the id is of a type a cursor cannot carry.
    /// </exception>
    /// <exception cref="Bsft.Core.ValidationException">
    /// Thrown before any query runs when the page size is below 1 (keyed on <c>pageSize</c>, carrying
    /// <see cref="KeysetPagingErrorCodes.InvalidPageSize"/>) or the cursor is not valid for the listing
    /// (keyed on <c>cursor</c>, carrying <see cref="KeysetPagingErrorCodes.InvalidCursor"/>).
    /// </exception>
    Task<CursorPagedResult<TItem>> ListAsync<TItem, TKey, TId>(
        IQueryable<TItem> query,
        KeysetOrder<TItem, TKey, TId> order,
        CursorPagedRequest request,
        CancellationToken cancellationToken = default);
}
