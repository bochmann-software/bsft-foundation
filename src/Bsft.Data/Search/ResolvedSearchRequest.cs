using Bsft.Data.Contracts.Search;

namespace Bsft.Data.Search;

/// <summary>
/// A search request resolved against the group it names: what to match, over which indexed fields,
/// and in which mode.
/// </summary>
/// <remarks>
/// The mode is resolved here rather than in the provider. A request may leave it unset and inherit the
/// group's default, and that is executor policy: every provider would otherwise have to reimplement
/// the same fallback identically, and a provider that got it wrong would silently answer a different
/// question than the one that was asked.
/// </remarks>
/// <param name="Group">The declared group being searched.</param>
/// <param name="Term">The search term.</param>
/// <param name="Mode">The resolved search mode.</param>
public sealed record ResolvedSearchRequest(SearchGroupConfiguration Group, string Term, SearchMode Mode);
