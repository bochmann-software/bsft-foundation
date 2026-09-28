using Bsft.Data.Contracts.Search;

namespace Bsft.Data.Search;

/// <summary>
/// The part of an <see cref="ISearchProvider"/> that is the same whatever the database is.
/// </summary>
/// <remarks>
/// Reporting the configuration problems of a set of groups is walking the set and collecting what each
/// one has to say; only what a provider has to say about one group differs. Written out per provider,
/// the walk was three copies of the same six lines, and three places for the argument check to be
/// forgotten in.
/// </remarks>
public abstract class SearchProviderBase : ISearchProvider
{
    /// <inheritdoc />
    public abstract IQueryable<RankedItem<TEntity>> ApplySearch<TEntity>(
        IQueryable<TEntity> source,
        ResolvedSearchRequest query);

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="groups"/> is null.</exception>
    public IReadOnlyList<string> DescribeConfigurationErrors(IEnumerable<SearchGroupConfiguration> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        return [.. groups.SelectMany(Describe)];
    }

    /// <summary>Names everything about one group that this provider cannot serve as declared.</summary>
    /// <param name="group">The declared group.</param>
    /// <returns>A description per problem found, or nothing when the group is serviceable.</returns>
    protected abstract IEnumerable<string> Describe(SearchGroupConfiguration group);
}
