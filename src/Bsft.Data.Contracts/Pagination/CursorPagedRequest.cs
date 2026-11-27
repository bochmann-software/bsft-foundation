namespace Bsft.Data.Contracts.Pagination;

/// <summary>
/// Represents a request for one page of a keyset (cursor) listing: the opaque cursor that names where the
/// previous page ended, and the size of the page.
/// </summary>
/// <param name="Cursor">
/// The <see cref="CursorPagedResult{T}.NextCursor"/> of the previous page, unchanged, or null for the first
/// page. The value is opaque: a client passes back what it received and never builds or edits one.
/// </param>
/// <param name="PageSize">The number of items per page. Defaults to 25.</param>
/// <remarks>
/// Keyset paging is the counterpart of <see cref="PagedRequest"/> for large or growing listings: the next
/// page is read after the last row of the previous one rather than by skipping an offset, so its cost does
/// not grow with the depth of the page, and rows inserted meanwhile do not shift the pages that follow.
/// It pages forward only, and the order is fixed by the listing rather than chosen per request.
/// </remarks>
public record CursorPagedRequest(
    string? Cursor = null,
    int PageSize = PagedRequest.DefaultPageSize)
{
    /// <summary>
    /// Gets a value indicating whether the answer carries the number of all items of the listing. Off by
    /// default, because counting a large table costs what keyset paging saves.
    /// </summary>
    /// <remarks>
    /// Deliberately not a positional parameter, for the same reason as <see cref="PagedRequest.Search"/>:
    /// adding one later would change the generated <c>Deconstruct</c> and the public constructor.
    /// </remarks>
    public bool IncludeTotalCount { get; init; }
}
