namespace Bsft.Data.Contracts.Search;

/// <summary>
/// The search part of a <see cref="Pagination.PagedRequest"/>.
/// </summary>
/// <param name="Group">The name of the declared search group to query.</param>
/// <param name="Term">The search term.</param>
/// <param name="Mode">The mode to use, or <c>null</c> for the group's default.</param>
/// <param name="IsOrderedByRank">
/// Whether results are ordered by relevance. When <c>false</c>, the sort criteria of the paged
/// request apply instead.
/// </param>
public sealed record SearchRequest(string Group, string Term, SearchMode? Mode = null, bool IsOrderedByRank = true);
