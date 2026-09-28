using Bsft.Data.Search;
using Bsft.Data.Postgres.Tests.Support;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Bsft.Data.Contracts.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Postgres.Tests;

/// <summary>
/// The provider is verified through the SQL it generates. No PostgreSQL instance is involved, so
/// runtime stemming and ranking quality are explicitly out of scope here — see docs/search.md.
/// </summary>
public class PostgresSearchProviderTests : IDisposable
{
    private readonly SearchTestContext _context = new();
    private readonly PostgresSearchProvider _provider = new(Options.Create(new BsftDataPostgresOptions().HasFullTextIndex("profile")));

    [Fact]
    public void TheSearchTermIsAParameter_NotAnInlinedLiteral()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        // An inlined literal would give every distinct search term its own statement text and its
        // own query plan — ruinous for a search-as-you-type endpoint.
        var statement = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
        statement.Should().Contain("@__");
        statement.Should().NotContain("müller");
    }

    [Fact]
    public void PrefixSearch_MatchesTheIndexedVectorAndNeverEmitsALeadingWildcardLike()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        sql.Should().Contain("to_tsquery(");
        sql.Should().Contain("@@");
        sql.Should().Contain("\"SearchVector\"");
        sql.Should().NotContain("LIKE");
    }

    [Fact]
    public void FreeTextSearch_GoesThroughWebSearchToTsQuery()
    {
        var sql = Search(SearchMode.FreeText, "erfahrener schweisser");

        // websearch_to_tsquery is defined to take whatever a user typed, quoted phrases and all,
        // without failing on syntax the way to_tsquery does.
        sql.Should().Contain("websearch_to_tsquery(");
        sql.Should().NotContain("LIKE");
    }

    [Fact]
    public void GermanStemming_IsRequestedThroughTheTextSearchConfiguration()
    {
        Search(SearchMode.FreeText, "schweisser").Should().Contain("'german'");
    }

    [Fact]
    public void AnUnmappableLanguage_LeavesTheQueryOnTheServerDefault()
    {
        // Startup validation refuses such a group, so this is the second lock rather than the first:
        // the query is still well-formed, it just names no configuration.
        var sql = Search(SearchMode.FreeText, "term", language: "klingon");

        sql.Should().Contain("websearch_to_tsquery(");
        sql.Should().NotContain("'german'");
    }

    [Fact]
    public void AnUnmappableLanguage_FailsStartupInsteadOfLosingStemmingSilently()
    {
        var errors = _provider.DescribeConfigurationErrors([Group(language: "klingon")]);

        errors.Should().ContainSingle().Which.Should().Contain("klingon").And.Contain("de");
    }

    [Fact]
    public void TheSupportedLanguageCodesAreListedInAStableOrder()
    {
        FullTextLanguageResolver.SupportedCodes.Should().Equal("de", "en", "es", "fr", "it", "nl");
    }

    [Fact]
    public void ARegisteredTextSearchConfigurationReachesTheQuery()
    {
        // The query has to name the same configuration the index was built with, or it probes the
        // GIN index with lexemes stemmed by different rules.
        var provider = new PostgresSearchProvider(Options.Create(new BsftDataPostgresOptions()
            .HasFullTextIndex("profile")
            .UseTextSearchConfiguration("de", "german_unaccent")));

        var sql = provider
            .ApplySearch(_context.Candidates, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix))
            .ToQueryString();

        sql.Should().Contain("'german_unaccent'").And.NotContain("'german'");
    }

    [Fact]
    public void ARegisteredLanguageCode_PassesStartupValidationAndIsOfferedInTheError()
    {
        var provider = new PostgresSearchProvider(Options.Create(new BsftDataPostgresOptions()
            .HasFullTextIndex("profile")
            .UseTextSearchConfiguration("pt", "portuguese")));

        provider.DescribeConfigurationErrors([Group(language: "pt")]).Should().BeEmpty();

        // The codes a deployment added are as usable as the built-in ones, so the message that lists
        // them has to say so rather than sending someone back to the six the framework ships.
        provider.DescribeConfigurationErrors([Group(language: "klingon")])
            .Should().ContainSingle().Which.Should().Contain("pt").And.Contain("UseTextSearchConfiguration");
    }

    [Fact]
    public void TheScoreIsTsRankWeightedByTheDeclaredFieldWeights()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        // ts_rank takes its weights as {D, C, B, A}, and the migration labels the first declared
        // field A. So Name's 2.0 lands last and Notes' 1.0 second to last.
        sql.Should().Contain("ts_rank(");
        sql.Should().Contain("ARRAY[0,0,1,2]");
    }

    [Fact]
    public void RankOrderingIsPushedDownIntoSql()
    {
        // What the executor does with every ranked search. The score is a projected member so that
        // EF can order by it in the database; if it could not, paging would order in memory over a
        // page that was already cut in SQL, which is silently wrong rather than merely slow.
        var hits = _provider.ApplySearch(_context.Candidates, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        var sql = hits.OrderByDescending(r => r.Score).ThenBy(r => r.Item.Id).ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql[sql.IndexOf("ORDER BY", StringComparison.Ordinal)..].Should().Contain("ts_rank(").And.Contain("DESC");
    }

    [Fact]
    public void ScopingAppliedByTheCallerSurvivesComposition()
    {
        var tenant = "tenant-a";
        var query = _context.Candidates.Where(c => c.TenantId == tenant);

        var sql = _provider.ApplySearch(query, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix)).ToQueryString();

        sql.Should().Contain("\"TenantId\"");
        sql.Should().Contain("@@");
    }

    [Theory]
    [InlineData("müller", SearchMode.Prefix, "müller:*")]
    [InlineData("  müller  ", SearchMode.Prefix, "müller:*")]
    [InlineData("mü\"ller", SearchMode.Prefix, "müller:*")]
    // Every word is anchored and the words are ANDed, so each further keystroke narrows the result.
    [InlineData("erfahrener schwei", SearchMode.Prefix, "erfahrener:* & schwei:*")]
    // to_tsquery reads &, |, ! and : as operators and fails on an unbalanced one, so prefix mode is
    // rebuilt from the term's own words rather than escaped.
    [InlineData("müller & schmidt", SearchMode.Prefix, "müller:* & schmidt:*")]
    [InlineData("AC*DC", SearchMode.Prefix, "AC:* & DC:*")]
    // websearch_to_tsquery is safe with whatever a user typed, so free text passes it through.
    [InlineData("erfahrener schweisser", SearchMode.FreeText, "erfahrener schweisser")]
    [InlineData("AC*DC", SearchMode.FreeText, "AC*DC")]
    // The quotes are websearch_to_tsquery's own phrase operator: dropping them would turn a request
    // for an exact phrase into a request for either word.
    [InlineData("\"erfahrener schweisser\"", SearchMode.FreeText, "\"erfahrener schweisser\"")]
    [InlineData("\"exact\" oder nicht", SearchMode.FreeText, "\"exact\" oder nicht")]
    public void TheSearchConditionIsBuiltWithoutALeadingWildcard(string term, SearchMode mode, string expected)
    {
        PostgresSearchProvider.BuildSearchCondition(term, mode).Should().Be(expected);
    }

    [Theory]
    [InlineData("", SearchMode.Prefix)]
    [InlineData("   ", SearchMode.Prefix)]
    // A quote is a separator to Words(), so a term of nothing but quotes carries no word.
    [InlineData("\"\"", SearchMode.Prefix)]
    [InlineData(" \" \" ", SearchMode.Prefix)]
    // The intuitive match-everything input carries no content either.
    [InlineData("*", SearchMode.Prefix)]
    [InlineData("***", SearchMode.Prefix)]
    [InlineData(" * ", SearchMode.Prefix)]
    [InlineData("\"*\"", SearchMode.Prefix)]
    // A term of nothing but tsquery operators would reach to_tsquery as a syntax error and
    // websearch_to_tsquery as an empty query that quietly matches nothing. Both are rejected here.
    [InlineData("& | !", SearchMode.Prefix)]
    [InlineData("& | !", SearchMode.FreeText)]
    [InlineData("*", SearchMode.FreeText)]
    [InlineData("\"\"", SearchMode.FreeText)]
    public void ATermWithNoUsableCharacters_IsRejected(string term, SearchMode mode)
    {
        var act = () => PostgresSearchProvider.BuildSearchCondition(term, mode);

        act.Should().Throw<ArgumentException>().WithParameterName("term");
    }

    [Fact]
    public void ADeclaredGroupWithoutAnIndex_IsReported()
    {
        var missing = _provider.DescribeConfigurationErrors([Group("unindexed")]);

        missing.Should().ContainSingle().Which.Should().Contain("unindexed").And.Contain("HasFullTextIndex");
    }

    [Fact]
    public void AnIndexedGroupInAKnownLanguage_IsNotReported()
    {
        _provider.DescribeConfigurationErrors([Group()]).Should().BeEmpty();
    }

    [Fact]
    public void AGroupWeightingMoreFieldsThanPostgresHasLabels_IsReported()
    {
        // A fifth field has no label left, so it would silently rank as the least important one.
        var errors = _provider.DescribeConfigurationErrors([Group(fields: 5)]);

        errors.Should().ContainSingle().Which.Should().Contain("A to D");
    }

    [Fact]
    public void AGroupWithoutADeclaredIndex_IsRefusedRatherThanSearchedUnindexed()
    {
        var provider = new PostgresSearchProvider(Options.Create(new BsftDataPostgresOptions()));

        var act = () => provider.ApplySearch(_context.Candidates, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        act.Should().Throw<InvalidOperationException>().WithMessage("*no tsvector column declared*");
    }

    [Fact]
    public void AnUnmappedVectorColumn_IsRefusedWithTheMappingThatFixesIt()
    {
        using var context = new UnmappedVectorContext();

        var act = () => _provider.ApplySearch(context.Candidates, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Property<NpgsqlTsVector>*");
    }

    [Fact]
    public void AVectorColumnMappedAsText_IsRefused()
    {
        // Matching text with @@ is not a slower search, it is a different one that does not use the
        // GIN index at all.
        using var context = new TextVectorContext();

        var act = () => _provider.ApplySearch(context.Candidates, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an NpgsqlTsVector*");
    }

    [Fact]
    public void AQueryableThatIsNotAnEfCoreQuery_IsRefusedRatherThanSearchingNothing()
    {
        var act = () => _provider.ApplySearch(
            new List<Candidate>().AsQueryable(),
            new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an EF Core query*");
    }

    [Fact]
    public void AnUnmappedEntity_IsRefused()
    {
        using var context = new SearchTestContext();
        var projected = context.Candidates.Select(c => new Referral { Id = c.Id, Name = c.Name });

        var act = () => _provider.ApplySearch(projected, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not mapped*");
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        FluentActions.Invoking(() => new PostgresSearchProvider(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        FluentActions.Invoking(() => _provider.ApplySearch<Candidate>(null!, new ResolvedSearchRequest(Group(), "x", SearchMode.Prefix)))
            .Should().Throw<ArgumentNullException>().WithParameterName("source");
        FluentActions.Invoking(() => _provider.ApplySearch(_context.Candidates, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("query");
        FluentActions.Invoking(() => _provider.DescribeConfigurationErrors(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("groups");
    }

    private string Search(SearchMode mode, string term, string language = "de") =>
        _provider
            .ApplySearch(_context.Candidates, new ResolvedSearchRequest(Group(language: language), term, mode))
            .ToQueryString();

    internal static SearchGroupConfiguration Group(string name = "profile", string language = "de", int fields = 2)
    {
        Expression<Func<Candidate, string?>> candidateName = c => c.Name;
        Expression<Func<Candidate, string?>> notes = c => c.Notes;

        var declared = new List<SearchField>
        {
            new("Name", candidateName, 2.0),
            new("Notes", notes, 1.0),
        };

        // Extra fields exist only to exercise the label limit; they select the same column.
        for (var index = declared.Count; index < fields; index++)
        {
            declared.Add(new SearchField($"Extra{index}", notes, 1.0));
        }

        return new SearchGroupConfiguration(
            name,
            typeof(Candidate),
            [.. declared.Take(fields)],
            SearchMode.Prefix,
            language);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
