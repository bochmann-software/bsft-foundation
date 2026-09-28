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
/// What <see cref="BsftDataSqliteOptions.UseRemoveDiacritics"/> actually does to a real index.
/// </summary>
/// <remarks>
/// The option travels into the engine as a word inside a tokenizer string, and a string is exactly the
/// kind of thing every assertion about generated SQL agrees with and no deployment does. So the level
/// is checked here the only way that can fail on a typo: by building the index with a non-default
/// level and asking the engine what it now matches. The framework's default of 2 is covered by
/// <see cref="SqliteFullTextSearchTests.DiacriticsAreFoldedInBothDirections"/>; this is its opposite.
/// </remarks>
public sealed class SqliteDiacriticFoldingTests : IDisposable
{
    private static readonly Guid _berlin = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SearchTestContext _context;

    public SqliteDiacriticFoldingTests()
    {
        _connection.Open();
        _context = new SearchTestContext(_connection);
        _context.Database.EnsureCreated();

        // The deployment that deliberately keeps accents apart, wired the way an application wires it:
        // through its own options, which the migration is handed.
        FullTextSchema.Apply(_context, options: SearchOptions());

        _context.Candidates.Add(
            new Candidate { Id = 1, Name = "Anna Müller", TenantId = "t1", OrganizationUnitId = _berlin });
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
            .UseRemoveDiacritics(0);

    [Fact]
    public async Task AnIndexBuiltWithoutFoldingKeepsAccentsApart()
    {
        // The whole point of lowering the level: the plain-keyboard spelling no longer reaches the
        // accented name. With the framework's default it would.
        (await SearchAsync("muller")).Should().BeEmpty();
    }

    [Fact]
    public async Task AnIndexBuiltWithoutFoldingStillFindsTheAccentedSpelling()
    {
        // The control that keeps the test honest: an index nobody can search at all would satisfy the
        // assertion above just as well.
        (await SearchAsync("müller")).Should().BeEquivalentTo([1]);
    }

    private async Task<IReadOnlyList<int>> SearchAsync(string term)
    {
        var executor = new SearchExecutor<Candidate, CandidateDto>(
            new CompiledSearchConfiguration<Candidate, CandidateDto>(new CandidateQueryConfiguration()),
            new NullTelemetry(),
            new SqliteSearchProvider(Options.Create(SearchOptions())),
            new FixedScopeProvider(new SearchScopeState("t1", [_berlin])));

        var result = await executor.SearchAsync(
            _context.Candidates, new PagedRequest { Search = new SearchRequest("profile", term, SearchMode.Prefix) });

        return [.. result.Items.Select(i => i.Item.Id)];
    }
}
