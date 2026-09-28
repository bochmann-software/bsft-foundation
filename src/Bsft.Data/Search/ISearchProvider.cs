using Bsft.Data.Contracts.Search;

namespace Bsft.Data.Search;

/// <summary>
/// Translates a search request into a provider-specific, index-backed query.
/// </summary>
/// <remarks>
/// This is the only place in the framework where search SQL is produced. There is deliberately no
/// default implementation and no fallback: without a registered provider a declared search group
/// fails startup validation rather than silently degrading to <c>LIKE '%…%'</c>, which is the
/// table-scanning behaviour the whole feature exists to prevent.
/// </remarks>
public interface ISearchProvider
{
    /// <summary>
    /// Applies the search to a queryable and projects each row to its relevance score.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The source queryable, already scoped by tenant and organisation.</param>
    /// <param name="query">The search resolved against its declared group.</param>
    /// <returns>A queryable of scored hits.</returns>
    IQueryable<RankedItem<TEntity>> ApplySearch<TEntity>(IQueryable<TEntity> source, ResolvedSearchRequest query);

    /// <summary>
    /// Names everything about the declared groups that this provider cannot serve as declared. A
    /// non-empty result fails startup.
    /// </summary>
    /// <remarks>
    /// A missing index is the obvious case, but not the only one: a stemming language the provider
    /// cannot map degrades just as silently, and only the provider knows which languages it has. Both
    /// are answered here so that a provider-specific misconfiguration cannot reach production
    /// unreported.
    /// </remarks>
    /// <param name="groups">All declared search groups.</param>
    /// <returns>A description per problem found.</returns>
    IReadOnlyList<string> DescribeConfigurationErrors(IEnumerable<SearchGroupConfiguration> groups);
}
