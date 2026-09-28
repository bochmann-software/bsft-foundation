using Bsft.Data.Search;
using Bsft.Data.SqlServer.Tests.Support;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Bsft.Data.Contracts.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.SqlServer.Tests;

/// <summary>
/// The provider is verified through the SQL it generates. No SQL Server instance is involved, so
/// runtime stemming and ranking quality are explicitly out of scope here — see docs/search.md.
/// </summary>
public class SqlServerSearchProviderTests : IDisposable
{
    private readonly SearchTestContext _context = new();
    private readonly SqlServerSearchProvider _provider = new(Options.Create(new BsftDataSqlServerOptions().HasFullTextIndex("profile")));

    [Fact]
    public void TheSearchTermIsAParameter_NotAnInlinedLiteral()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        // An inlined literal would give every distinct search term its own statement text and its
        // own query plan — ruinous for a search-as-you-type endpoint. ToQueryString prints the
        // parameter value in a leading DECLARE, so the predicate itself is what has to be checked.
        var predicate = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        predicate.Should().Contain("@__");
        predicate.Should().NotContain("müller");
    }

    [Fact]
    public void PrefixSearch_EmitsContainsAndNeverALeadingWildcardLike()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        sql.Should().Contain("CONTAINS");
        sql.Should().NotContain("LIKE");
    }

    [Fact]
    public void FreeTextSearch_EmitsFreeText()
    {
        var sql = Search(SearchMode.FreeText, "erfahrener schweisser");

        sql.Should().Contain("FREETEXT");
        sql.Should().NotContain("LIKE");
    }

    [Fact]
    public void GermanStemming_IsRequestedThroughTheLocaleIdentifier()
    {
        var sql = Search(SearchMode.FreeText, "schweisser", language: "de");

        sql.Should().Contain("1031");
    }

    [Fact]
    public void AnUnmappableLanguage_LeavesTheQueryWithoutALanguageClause()
    {
        // Startup validation refuses such a group, so this is the second lock rather than the first:
        // the query is still well-formed, it just carries no stemming language.
        var sql = Search(SearchMode.FreeText, "term", language: "klingon");

        sql.Should().Contain("FREETEXT");
        sql.Should().NotContain("LANGUAGE");
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
    public void EveryDeclaredFieldParticipatesInTheMatch()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        sql.Should().Contain("[Name]");
        sql.Should().Contain("[Notes]");
    }

    [Fact]
    public void TheScoreIsAWeightedFieldMatch()
    {
        var sql = Search(SearchMode.Prefix, "müller");

        // A weighted CASE per field, summed: two fields with weights 2 and 1.
        sql.Should().Contain("CASE");
        sql.Should().Contain("2.0");
    }

    [Fact]
    public void ScopingAppliedByTheCallerSurvivesComposition()
    {
        var tenant = "tenant-a";
        var query = _context.Candidates.Where(c => c.TenantId == tenant);

        var sql = _provider.ApplySearch(query, new ResolvedSearchRequest(Group(), "müller", SearchMode.Prefix)).ToQueryString();

        sql.Should().Contain("[TenantId]");
        sql.Should().Contain("CONTAINS");
    }

    [Theory]
    [InlineData("müller", SearchMode.Prefix, "\"müller*\"")]
    [InlineData("  müller  ", SearchMode.Prefix, "\"müller*\"")]
    [InlineData("mü\"ller", SearchMode.Prefix, "\"müller*\"")]
    [InlineData("erfahrener schweisser", SearchMode.FreeText, "erfahrener schweisser")]
    // An asterisk between content characters is part of the band name, not syntax, so it survives.
    // SQL Server takes the anchor only as the last character of the term, so an asterisk the user
    // typed becomes a separator rather than a wildcard it cannot honour.
    [InlineData("AC*DC", SearchMode.Prefix, "\"AC DC*\"")]
    [InlineData("*foo", SearchMode.Prefix, "\"foo*\"")]
    [InlineData("foo*", SearchMode.Prefix, "\"foo*\"")]
    [InlineData("AC*DC", SearchMode.FreeText, "AC*DC")]
    public void TheSearchConditionIsBuiltWithoutALeadingWildcard(string term, SearchMode mode, string expected)
    {
        SqlServerSearchProvider.BuildSearchCondition(term, mode).Should().Be(expected);
    }

    [Theory]
    [InlineData("", SearchMode.Prefix)]
    [InlineData("   ", SearchMode.Prefix)]
    // Nothing survives the quote stripping, which would otherwise reach the server as the empty
    // prefix "*" and fail there as a syntax error.
    [InlineData("\"\"", SearchMode.Prefix)]
    [InlineData(" \" \" ", SearchMode.Prefix)]
    // The intuitive match-everything input. It carries no content either, and would otherwise be
    // anchored into the same degenerate prefix "**".
    [InlineData("*", SearchMode.Prefix)]
    [InlineData("***", SearchMode.Prefix)]
    [InlineData(" * ", SearchMode.Prefix)]
    [InlineData("\"*\"", SearchMode.Prefix)]
    // FreeText would take the same contentless term as a phrase and quietly match nothing, so the
    // rejection is the same in both modes rather than a Prefix-only guard.
    [InlineData("*", SearchMode.FreeText)]
    [InlineData("\"\"", SearchMode.FreeText)]
    public void ATermWithNoUsableCharacters_IsRejected(string term, SearchMode mode)
    {
        var act = () => SqlServerSearchProvider.BuildSearchCondition(term, mode);

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
    public void ArgumentsAreValidated()
    {
        FluentActions.Invoking(() => new SqlServerSearchProvider(null!))
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

    private static SearchGroupConfiguration Group(string name = "profile", string language = "de")
    {
        Expression<Func<Candidate, string?>> name1 = c => c.Name;
        Expression<Func<Candidate, string?>> notes = c => c.Notes;

        return new SearchGroupConfiguration(
            name,
            typeof(Candidate),
            [new SearchField("Name", name1, 2.0), new SearchField("Notes", notes, 1.0)],
            SearchMode.Prefix,
            language);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
