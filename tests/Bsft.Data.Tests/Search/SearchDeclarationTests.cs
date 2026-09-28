using Bsft.Data.Filtering;
using Bsft.Data.Search;
using Bsft.Data.Contracts.Search;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Search;

public class SearchDeclarationTests
{
    [Fact]
    public void AGroupWithoutFieldsIsRejected()
    {
        var builder = new QueryConfigurationBuilder<Candidate, CandidateDto>();
        builder.Searchable("profile", _ => { });

        var act = () => builder.BuildSearchGroups();

        act.Should().Throw<InvalidOperationException>().WithMessage("*profile*");
    }

    [Fact]
    public void TheGroupNameIsRequired()
    {
        var builder = new QueryConfigurationBuilder<Candidate, CandidateDto>();

        FluentActions.Invoking(() => builder.Searchable(" ", _ => { }))
            .Should().Throw<ArgumentException>().WithParameterName("name");
        FluentActions.Invoking(() => builder.Searchable("profile", null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void AFieldMustBeASimplePropertyAccess()
    {
        var builder = new SearchGroupBuilderProbe();

        FluentActions.Invoking(() => builder.Declare(s => s.Field(c => c.Name + c.Notes)))
            .Should().Throw<ArgumentException>().WithParameterName("property");
        FluentActions.Invoking(() => builder.Declare(s => s.Field(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("property");
    }

    [Fact]
    public void AFieldWeightMustBePositive()
    {
        var builder = new SearchGroupBuilderProbe();

        FluentActions.Invoking(() => builder.Declare(s => s.Field(c => c.Name, weight: 0)))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("weight");
    }

    [Fact]
    public void TheLanguageCodeIsRequired()
    {
        var builder = new SearchGroupBuilderProbe();

        FluentActions.Invoking(() => builder.Declare(s => s.Field(c => c.Name).Language(" ")))
            .Should().Throw<ArgumentException>().WithParameterName("language");
    }

    [Fact]
    public void ADuplicateGroupNameIsRejectedWhereItIsMade()
    {
        var builder = new QueryConfigurationBuilder<Candidate, CandidateDto>();
        builder.Searchable("profile", s => s.Field(c => c.Name));

        // Otherwise this surfaces as a dictionary collision inside the executor on the first search.
        FluentActions.Invoking(() => builder.Searchable("profile", s => s.Field(c => c.Notes)))
            .Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void ADuplicateFieldIsRejectedInsteadOfDoublingItsWeight()
    {
        var builder = new QueryConfigurationBuilder<Candidate, CandidateDto>();

        FluentActions.Invoking(() => builder.Searchable("profile", s => s.Field(c => c.Name).Field(c => c.Name)))
            .Should().Throw<ArgumentException>().WithParameterName("property");
    }

    [Fact]
    public void TheDefaultsArePrefixSearchInEnglish()
    {
        var builder = new QueryConfigurationBuilder<Candidate, CandidateDto>();
        builder.Searchable("profile", s => s.Field(c => c.Name));

        var group = builder.BuildSearchGroups().Should().ContainSingle().Subject;
        group.DefaultMode.Should().Be(SearchMode.Prefix);
        group.Language.Should().Be("en");
        group.EntityType.Should().Be<Candidate>();
        group.Fields[0].Weight.Should().Be(1.0);
    }

    [Fact]
    public void AConfigurationWithoutASearchableDeclarationHasNoGroups()
    {
        var builder = new QueryConfigurationBuilder<Candidate, CandidateDto>();
        new PlainQueryConfiguration().Configure(builder);

        builder.BuildSearchGroups().Should().BeEmpty();
    }

    /// <summary>Lets a test drive the group builder without going through a full configuration.</summary>
    private sealed class SearchGroupBuilderProbe
    {
        public void Declare(Action<SearchGroupBuilder<Candidate>> configure)
        {
            var builder = new QueryConfigurationBuilder<Candidate, CandidateDto>();
            builder.Searchable("profile", configure);
        }
    }
}
