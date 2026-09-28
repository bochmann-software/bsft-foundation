using Bsft.Data.Search;
using Bsft.Data.Sqlite.Tests.Support;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Bsft.Data.Contracts.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Sqlite.Tests;

/// <summary>
/// The translation is verified through the SQL the provider generates. What that SQL <em>does</em> in
/// a real engine is verified separately and for real in <see cref="SqliteFullTextSearchTests"/>,
/// which is the one thing SQLite makes possible and no other provider in this repository can offer.
/// </summary>
public class SqliteSearchProviderTests : IDisposable
{
    private readonly SearchTestContext _context = new();
    private readonly SqliteSearchProvider _provider = new(Options.Create(SearchOptions()));

    [Fact]
    public void TheSearchTermIsAParameter_NotAnInlinedLiteral()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        // An inlined literal would give every distinct search term its own statement text and its
        // own query plan — ruinous for a search-as-you-type endpoint. ToQueryString prints the
        // parameter value in a leading .param set line, so the statement itself is what has to be
        // checked.
        var statement = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
        statement.Should().Contain("@__");
        statement.Should().NotContain("müller");
    }

    [Fact]
    public void PrefixSearch_MatchesTheIndexAndNeverEmitsALeadingWildcardLike()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        sql.Should().Contain("match(");
        sql.Should().Contain("\"Candidates_fts\"");
        sql.Should().NotContain("LIKE");
    }

    [Fact]
    public void FreeTextSearch_MatchesTheIndexToo()
    {
        var sql = Search(SearchMode.FreeText, "erfahrener schweisser");

        sql.Should().Contain("match(");
        sql.Should().NotContain("LIKE");
    }

    [Fact]
    public void TheIndexIsJoinedByRowIdOntoTheCallersQueryable()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        // Joined, not queried beside: EF Core global query filters apply to a composed query and
        // would be bypassed by a statement built next to the entity set.
        sql.Should().Contain("INNER JOIN");
        sql.Should().Contain("\"rowid\"");
    }

    [Fact]
    public void TheScoreIsFts5sOwnBm25WeightedByTheDeclaredFieldWeights()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        sql.Should().Contain("bm25(");
        sql.Should().Contain("2.0, 1.0");
    }

    [Fact]
    public void ScopingAppliedByTheCallerSurvivesComposition()
    {
        var tenant = "tenant-a";
        var query = _context.Candidates.Where(c => c.TenantId == tenant);

        var sql = _provider.ApplySearch(query, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix)).ToQueryString();

        sql.Should().Contain("\"TenantId\"");
        sql.Should().Contain("match(");
    }

    [Theory]
    [InlineData("müller", SearchMode.Prefix, "\"müller\"*")]
    [InlineData("  müller  ", SearchMode.Prefix, "\"müller\"*")]
    [InlineData("mü\"ller", SearchMode.Prefix, "\"müller\"*")]
    // A phrase anchors its last token, so a half-typed second word still finds the row.
    [InlineData("erfahrener schwei", SearchMode.Prefix, "\"erfahrener schwei\"*")]
    // Free text asks the same question SQL Server's FREETEXT does: any of these words.
    [InlineData("erfahrener schweisser", SearchMode.FreeText, "\"erfahrener\" OR \"schweisser\"")]
    [InlineData("müller", SearchMode.FreeText, "\"müller\"")]
    // An asterisk between content characters is part of the band name, not syntax, so it survives.
    [InlineData("AC*DC", SearchMode.Prefix, "\"AC*DC\"*")]
    [InlineData("AC*DC", SearchMode.FreeText, "\"AC*DC\"")]
    // A token that is nothing but the prefix anchor carries no content and is dropped rather than
    // emitted as an empty phrase, which FTS5 would reject outright.
    [InlineData("müller *", SearchMode.FreeText, "\"müller\"")]
    public void TheSearchConditionIsBuiltWithoutALeadingWildcard(string term, SearchMode mode, string expected)
    {
        SqliteSearchProvider.BuildSearchCondition(term, mode).Should().Be(expected);
    }

    [Theory]
    [InlineData("", SearchMode.Prefix)]
    [InlineData("   ", SearchMode.Prefix)]
    // Nothing survives the quote stripping, which would otherwise reach the engine as a prefix on an
    // empty phrase and fail there as a syntax error.
    [InlineData("\"\"", SearchMode.Prefix)]
    [InlineData(" \" \" ", SearchMode.Prefix)]
    // The intuitive match-everything input. It carries no content either.
    [InlineData("*", SearchMode.Prefix)]
    [InlineData("***", SearchMode.Prefix)]
    [InlineData(" * ", SearchMode.Prefix)]
    [InlineData("\"*\"", SearchMode.Prefix)]
    // Free text would take the same contentless term as a phrase and quietly match nothing, so the
    // rejection is the same in both modes rather than a Prefix-only guard.
    [InlineData("*", SearchMode.FreeText)]
    [InlineData("\"\"", SearchMode.FreeText)]
    public void ATermWithNoUsableCharacters_IsRejected(string term, SearchMode mode)
    {
        var act = () => SqliteSearchProvider.BuildSearchCondition(term, mode);

        act.Should().Throw<ArgumentException>().WithParameterName("term");
    }

    [Fact]
    public void ADeclaredGroupWithoutAnIndex_IsReported()
    {
        var missing = _provider.DescribeConfigurationErrors([Group("unindexed")]);

        missing.Should().ContainSingle().Which.Should().Contain("unindexed").And.Contain("HasFullTextIndex");
    }

    [Fact]
    public void AnIndexedGroupWhoseLanguageIsAcceptedUnstemmed_IsNotReported()
    {
        _provider.DescribeConfigurationErrors([Group()]).Should().BeEmpty();
    }

    [Fact]
    public void ALanguageFts5CannotStem_FailsStartupInsteadOfBeingSearchedUnstemmedInSilence()
    {
        var provider = new SqliteSearchProvider(Options.Create(new BsftDataSqliteOptions().HasFullTextIndex("profile", "Candidates_fts")));

        var errors = provider.DescribeConfigurationErrors([Group()]);

        errors.Should().ContainSingle().Which.Should()
            .Contain("de").And.Contain("Porter").And.Contain("AcceptsUnstemmedLanguage");
    }

    [Fact]
    public void EnglishOnThePorterTokenizer_IsTheOneStemmedCombination()
    {
        var options = new BsftDataSqliteOptions()
            .HasFullTextIndex("profile", "Candidates_fts")
            .UseTokenizer(SqliteTokenizer.Porter);

        new SqliteSearchProvider(Options.Create(options)).DescribeConfigurationErrors([Group(language: "en")]).Should().BeEmpty();

        // The same tokenizer does nothing for German, which is a property of the engine and is
        // reported rather than hidden.
        new SqliteSearchProvider(Options.Create(options)).DescribeConfigurationErrors([Group()]).Should().ContainSingle();
    }

    [Fact]
    public void TheStemmedLanguageCodesAreListedInAStableOrder()
    {
        FullTextTokenization.StemmedCodes.Should().Equal("en");
    }

    [Fact]
    public void AGroupWithoutADeclaredIndex_IsRefusedRatherThanSearchedUnindexed()
    {
        var provider = new SqliteSearchProvider(Options.Create(new BsftDataSqliteOptions()));

        var act = () => provider.ApplySearch(_context.Candidates, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        act.Should().Throw<InvalidOperationException>().WithMessage("*no FTS5 index declared*");
    }

    [Fact]
    public void AnUnmappedIndexTable_IsRefusedWithTheCallThatMapsIt()
    {
        using var context = new UnmappedIndexContext();

        var act = () => _provider.ApplySearch(context.Candidates, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        act.Should().Throw<InvalidOperationException>().WithMessage("*HasBsftFullTextIndex*");
    }

    [Fact]
    public void IndexColumnsInADifferentOrderThanTheGroupsFields_AreRefusedRatherThanRankedWrong()
    {
        // bm25 weights positionally, so this would run and rank Notes above Name without a trace.
        using var context = new SwappedColumnsContext();

        var act = () => _provider.ApplySearch(context.Candidates, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix));

        act.Should().Throw<InvalidOperationException>().WithMessage("*positionally*");
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
    public void ArgumentsAreValidated()
    {
        FluentActions.Invoking(() => new SqliteSearchProvider(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        FluentActions.Invoking(() => _provider.ApplySearch<Candidate>(null!, new ResolvedSearchRequest(Group(), "x", SearchMode.Prefix)))
            .Should().Throw<ArgumentNullException>().WithParameterName("source");
        FluentActions.Invoking(() => _provider.ApplySearch(_context.Candidates, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("query");
        FluentActions.Invoking(() => _provider.DescribeConfigurationErrors(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("groups");
    }

    internal static BsftDataSqliteOptions SearchOptions() =>
        new BsftDataSqliteOptions()
            .HasFullTextIndex("profile", "Candidates_fts")
            .AcceptsUnstemmedLanguage("de");

    internal static SearchGroupConfiguration Group(string name = "profile", string language = "de")
    {
        Expression<Func<Candidate, string?>> candidateName = c => c.Name;
        Expression<Func<Candidate, string?>> notes = c => c.Notes;

        return new SearchGroupConfiguration(
            name,
            typeof(Candidate),
            [new SearchField("Name", candidateName, 2.0), new SearchField("Notes", notes, 1.0)],
            SearchMode.Prefix,
            language);
    }

    private string Search(SearchMode mode, string term) =>
        _provider
            .ApplySearch(_context.Candidates, new ResolvedSearchRequest(Group(), term, mode))
            .ToQueryString();

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
