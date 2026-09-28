using Bsft.Data.Search;
using Bsft.Data.Contracts.Search;

namespace Bsft.Data.Tests.Support;

/// <summary>
/// A provider standing in for the SQL Server one. It matches with <c>string.Contains</c> so the
/// executor can be exercised on the EF in-memory provider; the real SQL is asserted in
/// Bsft.Data.SqlServer.Tests.
/// </summary>
public sealed class FakeSearchProvider : ISearchProvider
{
    public List<string> ConfigurationErrors { get; } = [];

    /// <summary>Gets the query the executor last handed over, so the resolution can be asserted.</summary>
    public ResolvedSearchRequest? LastQuery { get; private set; }

    public IQueryable<RankedItem<TEntity>> ApplySearch<TEntity>(IQueryable<TEntity> source, ResolvedSearchRequest query)
    {
        LastQuery = query;

        var candidates = (IQueryable<Candidate>)(object)source;
        var term = query.Term;

        var hits = candidates
            .Where(c => c.Name.Contains(term) || (c.Notes != null && c.Notes.Contains(term)))
            .Select(c => new RankedItem<Candidate> { Item = c, Score = c.Name.Contains(term) ? 2.0 : 1.0 });

        return (IQueryable<RankedItem<TEntity>>)(object)hits;
    }

    public IReadOnlyList<string> DescribeConfigurationErrors(IEnumerable<SearchGroupConfiguration> groups) =>
        ConfigurationErrors;
}
