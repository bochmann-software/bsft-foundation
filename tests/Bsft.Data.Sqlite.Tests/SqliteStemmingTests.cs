using Bsft.Data.Search;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using Bsft.Data.Sqlite.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Sqlite.Tests;

/// <summary>
/// What <see cref="BsftDataSqliteOptions.UseTokenizer"/> actually does to a real index.
/// </summary>
/// <remarks>
/// Stemming is the one capability the provider validates a group's language against at startup, so an
/// index built with a tokenizer other than the one the options name would have startup certify the
/// opposite of what the engine does. Asserting on the generated DDL cannot catch that on its own —
/// the assertion and the bug read the same options — so the tokenizer is checked here by asking the
/// engine for a match only a stemmer can produce.
/// </remarks>
public sealed class SqliteStemmingTests : IDisposable
{
    private static readonly Guid _berlin = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SearchTestContext _context;

    public SqliteStemmingTests()
    {
        _connection.Open();
        _context = new SearchTestContext(_connection);
        _context.Database.EnsureCreated();

        // The migration is handed the application's options and the plain command, which is the wiring
        // docs/search.md shows. Nothing here names the tokenizer a second time.
        FullTextSchema.Apply(_context, options: SearchOptions());

        _context.Candidates.Add(new Candidate
        {
            Id = 1,
            Name = "Anna Fischer",
            Notes = "running the Berlin office",
            TenantId = "t1",
            OrganizationUnitId = _berlin,
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static BsftDataSqliteOptions SearchOptions() =>
        new BsftDataSqliteOptions()
            .HasFullTextIndex("profile", "Candidates_fts")
            .AcceptsUnstemmedLanguage("de")
            .UseTokenizer(SqliteTokenizer.Porter);

    [Fact]
    public async Task AnIndexBuiltWithThePorterTokenizerMatchesTheStemOfAWordItIndexed()
    {
        // Porter stores "running" as "run" and reduces the query the same way. Under unicode61 the
        // term stays "running" and this free-text search finds nothing — which is what the index was
        // silently built as while the provider went on certifying that Porter stems English.
        (await SearchAsync("run")).Should().BeEquivalentTo([1]);
    }

    [Fact]
    public async Task AnIndexBuiltWithThePorterTokenizerStillFindsTheWholeWord()
    {
        // The control: an index that matched everything would satisfy the assertion above too.
        (await SearchAsync("running")).Should().BeEquivalentTo([1]);
        (await SearchAsync("walking")).Should().BeEmpty();
    }

    private async Task<IReadOnlyList<int>> SearchAsync(string term)
    {
        var executor = new SearchExecutor<Candidate, CandidateDto>(
            new CompiledSearchConfiguration<Candidate, CandidateDto>(new CandidateQueryConfiguration()),
            new NullTelemetry(),
            new SqliteSearchProvider(Options.Create(SearchOptions())),
            new FixedScopeProvider(new SearchScopeState("t1", [_berlin])));

        var result = await executor.SearchAsync(
            _context.Candidates,
            new PagedRequest { Search = new SearchRequest("profile", term, SearchMode.FreeText) });

        return [.. result.Items.Select(i => i.Item.Id)];
    }
}
