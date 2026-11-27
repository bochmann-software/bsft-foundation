using System.Text.Json.Serialization;

namespace Bsft.Data.Contracts.Pagination;

/// <summary>
/// Represents one page of a keyset (cursor) listing.
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
/// <param name="Items">The items on the current page, in the listing's order.</param>
/// <param name="PageSize">The page size the page was read with.</param>
/// <param name="NextCursor">
/// The opaque cursor of the next page, or null on the last page. Passing it back as
/// <see cref="CursorPagedRequest.Cursor"/> reads the page that follows. Left out of the JSON when null, so
/// its absence is what marks the last page.
/// </param>
/// <remarks>
/// The counterpart of <see cref="PagedResult{T}"/> for <see cref="CursorPagedRequest"/>. There is no page
/// number, because a keyset page has none, and no separate "has next page" flag, because a next cursor is
/// exactly that.
/// </remarks>
public record CursorPagedResult<T>(
    IReadOnlyList<T> Items,
    int PageSize,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor)
{
    /// <summary>
    /// Gets the number of all items of the listing, or null when the request did not ask for it
    /// (<see cref="CursorPagedRequest.IncludeTotalCount"/>). Left out of the JSON when null.
    /// </summary>
    /// <remarks>
    /// A 64-bit count, because keyset paging exists for the tables whose size outgrows an offset.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalCount { get; init; }
}
