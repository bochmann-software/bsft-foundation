using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Search;

namespace Bsft.Data.Contracts.Pagination;

/// <summary>
/// Represents a request for a paged, filtered, and sorted result set.
/// </summary>
/// <param name="Filter">An optional filter AST to apply.</param>
/// <param name="Sort">
/// An optional list of sort criteria. Read-only: the order a page was read in has to be the order the
/// continuation is read in, and a list the caller can still append to after the query was built makes
/// that a coincidence.
/// </param>
/// <param name="Page">The page number (1-based). Defaults to 1.</param>
/// <param name="PageSize">The number of items per page. Defaults to 25.</param>
public record PagedRequest(
    FilterNode? Filter = null,
    IReadOnlyList<SortItem>? Sort = null,
    int Page = 1,
    int PageSize = PagedRequest.DefaultPageSize)
{
    /// <summary>The page size used when a request does not state one.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// Gets an optional full-text search. Search, filter, sort and paging compose: the search narrows
    /// the result set, the filter narrows it further, and paging applies to the combined result.
    /// </summary>
    /// <remarks>
    /// Deliberately not a positional parameter. Adding one would change the generated
    /// <c>Deconstruct</c> and the public constructor signature, breaking callers that deconstruct the
    /// record and any assembly compiled against the previous version.
    /// </remarks>
    public SearchRequest? Search { get; init; }
}
