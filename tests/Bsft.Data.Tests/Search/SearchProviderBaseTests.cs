using Bsft.Data.Contracts.Search;
using Bsft.Data.Search;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Search;

/// <summary>
/// The walk over the declared groups is the part every provider shares, so it is pinned once here
/// rather than three times over the provider packages.
/// </summary>
public class SearchProviderBaseTests
{
    [Fact]
    public void DescribeConfigurationErrors_NullGroups_ThrowsArgumentNullException()
    {
        var provider = new RecordingSearchProvider();

        var act = () => provider.DescribeConfigurationErrors(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("groups");
    }

    [Fact]
    public void DescribeConfigurationErrors_NoGroups_ReportsNothing()
        => new RecordingSearchProvider().DescribeConfigurationErrors([]).Should().BeEmpty();

    [Fact]
    public void DescribeConfigurationErrors_SeveralGroups_ReportsWhatEachOneHasToSay()
    {
        // Flattened rather than grouped: the host reads one list of problems, not a list per group.
        var provider = new RecordingSearchProvider();

        var errors = provider.DescribeConfigurationErrors([Group("people"), Group("places")]);

        errors.Should().Equal("people is a problem", "people is also a problem", "places is a problem");
    }

    private static SearchGroupConfiguration Group(string name) =>
        new(name, typeof(object), [], SearchMode.Prefix, "en");

    private sealed class RecordingSearchProvider : SearchProviderBase
    {
        public override IQueryable<RankedItem<TEntity>> ApplySearch<TEntity>(
            IQueryable<TEntity> source,
            ResolvedSearchRequest query) => throw new NotSupportedException();

        protected override IEnumerable<string> Describe(SearchGroupConfiguration group)
        {
            yield return $"{group.Name} is a problem";

            if (group.Name == "people")
                yield return $"{group.Name} is also a problem";
        }
    }
}
