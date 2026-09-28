using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Search;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using Bsft.Data.Sqlite.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Sqlite.Tests;

/// <summary>
/// The provider against a real FTS5 index, through the whole executor pipeline.
/// </summary>
/// <remarks>
/// SQLite is the one engine this repository can test for real: it runs in-process and
/// Microsoft.Data.Sqlite ships FTS5. So this suite does not assert generated SQL — it creates the
/// index with the shipped migration helpers, writes through the shipped triggers, and asserts what
/// the engine actually returns and in which order. Everything the SQL Server provider has to list as
/// unverified — that the predicate matches the rows it should, that ranking ranks, that an index
/// stays in step with its table — is checked here for real.
/// </remarks>
public sealed class SqliteFullTextSearchTests : IDisposable
{
    private static readonly Guid _berlin = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _hamburg = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _koeln = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SearchTestContext _context;

    public SqliteFullTextSearchTests()
    {
        _connection.Open();
        _context = new SearchTestContext(_connection);
        _context.Database.EnsureCreated();
        FullTextSchema.Apply(_context);

        _context.Candidates.AddRange(
            new Candidate { Id = 1, Name = "Anna Müller", Notes = "erfahrene Schweisserin", TenantId = "t1", OrganizationUnitId = _berlin },
            new Candidate { Id = 2, Name = "Bernd Schmidt", Notes = "kennt Müller gut", TenantId = "t1", OrganizationUnitId = _berlin },
            new Candidate { Id = 3, Name = "Clara Müller", Notes = null, TenantId = "t1", OrganizationUnitId = _hamburg },
            new Candidate { Id = 4, Name = "Dora Müller", Notes = null, TenantId = "t2", OrganizationUnitId = _berlin },
            new Candidate { Id = 5, Name = "Egon Müller", Notes = null, TenantId = "t1", OrganizationUnitId = _koeln });
        _context.SaveChanges();
    }

    [Fact]
    public async Task APrefixSearchFindsTheRowsTheTriggersIndexed()
    {
        var hits = await SearchAsync("müll", SearchMode.Prefix);

        // 4 is another tenant's and 5 another branch's; both are outside the scope and neither is a
        // near miss the ranking could rescue.
        hits.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task TheIndexRowsReadBackKeyedByTheRowidOfTheRowTheyDescribe()
    {
        // The shared-type mapping is what the provider joins through; read on its own, each index row
        // carries the integer key of its base row and the hidden column the engine names the index by.
        var rows = await _context.Set<BsftFullTextIndexRow>("Candidates_fts").AsNoTracking().ToListAsync();

        rows.Select(row => row.RowId).Should().BeEquivalentTo([1L, 2L, 3L, 4L, 5L]);
        rows.Should().OnlyContain(row => row.Match != null);
    }

    [Fact]
    public async Task TheRankingIsRealBm25WeightedByTheDeclaredFieldWeights()
    {
        var result = await ExecuteAsync(new PagedRequest { Search = new SearchRequest("profile", "müll", SearchMode.Prefix) });

        // Name is declared at weight 2 and Notes at 1, so the two rows matching on the name outrank
        // the one that only mentions a Müller in its notes. Which of the two leads is bm25's call
        // about document length, and asserting it would be asserting the engine's tuning rather than
        // this package's behaviour.
        result.Items.Select(i => i.Item.Id).Take(2).Should().BeEquivalentTo([1, 3]);
        result.Items[^1].Item.Id.Should().Be(2);
        result.Items.Select(i => i.Score).Should().BeInDescendingOrder();
        result.Items.Should().OnlyContain(i => i.Score > 0);
    }

    [Fact]
    public async Task TenantAndOrganisationalScopingComposeWithTheIndexMatch()
    {
        var berlinOnly = await SearchAsync("müll", SearchMode.Prefix, new SearchScopeState("t1", [_berlin]));

        berlinOnly.Should().BeEquivalentTo([1, 2]);

        var otherTenant = await SearchAsync("müll", SearchMode.Prefix, new SearchScopeState("t2", [_berlin, _hamburg, _koeln]));

        otherTenant.Should().BeEquivalentTo([4]);
    }

    [Fact]
    public async Task AnUpdateKeepsTheIndexInStep()
    {
        var candidate = await _context.Candidates.SingleAsync(c => c.Id == 3);
        candidate.Name = "Clara Neumann";
        await _context.SaveChangesAsync();

        (await SearchAsync("müll", SearchMode.Prefix)).Should().BeEquivalentTo([1, 2]);
        (await SearchAsync("neumann", SearchMode.Prefix)).Should().BeEquivalentTo([3]);
    }

    [Fact]
    public async Task ADeleteKeepsTheIndexInStep()
    {
        _context.Candidates.Remove(await _context.Candidates.SingleAsync(c => c.Id == 1));
        await _context.SaveChangesAsync();

        (await SearchAsync("müll", SearchMode.Prefix)).Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public async Task AnInsertIsFindableImmediately()
    {
        // Unlike SQL Server, whose index is populated asynchronously, an FTS5 index maintained by
        // triggers is updated inside the same transaction as the write.
        _context.Candidates.Add(new Candidate
        {
            Id = 6,
            Name = "Frida Müllermeier",
            TenantId = "t1",
            OrganizationUnitId = _berlin,
        });
        await _context.SaveChangesAsync();

        (await SearchAsync("müllermeier", SearchMode.Prefix)).Should().BeEquivalentTo([6]);
    }

    [Fact]
    public async Task FreeTextMatchesAnyOfTheWords()
    {
        var hits = await SearchAsync("schweisserin schmidt", SearchMode.FreeText);

        // Any of these words, the way SQL Server's FREETEXT answers it. FTS5's own default is AND
        // between tokens, which would find neither row.
        hits.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task DiacriticsAreFoldedInBothDirections()
    {
        (await SearchAsync("muller", SearchMode.Prefix)).Should().BeEquivalentTo([1, 2, 3]);
        (await SearchAsync("müller", SearchMode.Prefix)).Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task GermanIsMatchedByExactTokens_WhichIsWhyTheLanguageHasToBeAcknowledged()
    {
        // The honest boundary, asserted rather than described: FTS5 has no German stemmer, so the
        // stem of a declared-German group does not match its inflections. This is what
        // AcceptsUnstemmedLanguage("de") signs off on, and what makes the acknowledgement mandatory.
        (await SearchAsync("schweisserin", SearchMode.FreeText)).Should().BeEquivalentTo([1]);
        (await SearchAsync("schweisser", SearchMode.FreeText)).Should().BeEmpty();

        // Prefix mode is what covers the inflection in practice.
        (await SearchAsync("schweisser", SearchMode.Prefix)).Should().BeEquivalentTo([1]);
    }

    [Fact]
    public async Task RankOrderingIsStableAcrossPages()
    {
        var search = new SearchRequest("profile", "müll", SearchMode.Prefix);

        var first = await ExecuteAsync(new PagedRequest(Page: 1, PageSize: 2) { Search = search });
        var second = await ExecuteAsync(new PagedRequest(Page: 2, PageSize: 2) { Search = search });

        first.TotalCount.Should().Be(3);
        first.Items.Should().HaveCount(2);
        second.Items.Should().HaveCount(1);
        first.Items.Select(i => i.Item.Id).Should().NotIntersectWith(second.Items.Select(i => i.Item.Id));
    }

    [Fact]
    public async Task WithoutRankOrderingTheRequestedSortSurvivesTheIndexJoin()
    {
        // The executor sorts before the provider composes its join. That the ordering survives being
        // joined onto is a property of the generated SQL that only a real engine can confirm.
        var request = new PagedRequest(Sort: [new SortItem(nameof(CandidateDto.Id), SortDirection.Desc)])
        {
            Search = new SearchRequest("profile", "müll", SearchMode.Prefix, IsOrderedByRank: false),
        };

        var result = await ExecuteAsync(request);

        result.Items.Select(i => i.Item.Id).Should().Equal(3, 2, 1);
    }

    [Fact]
    public async Task SearchComposesWithAStructuredFilter()
    {
        var filter = new ComparisonNode
        {
            Field = nameof(CandidateDto.Name),
            Op = FilterOperator.Eq,
            Value = System.Text.Json.JsonSerializer.SerializeToElement("Bernd Schmidt"),
        };

        var result = await ExecuteAsync(new PagedRequest(filter)
        {
            Search = new SearchRequest("profile", "müll", SearchMode.Prefix),
        });

        result.Items.Should().ContainSingle().Which.Item.Id.Should().Be(2);
    }

    [Fact]
    public async Task ATermWithNoUsableCharactersNeverReachesTheEngine()
    {
        var act = async () => await ExecuteAsync(new PagedRequest { Search = new SearchRequest("profile", "*") });

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("term");
    }

    private async Task<IReadOnlyList<int>> SearchAsync(string term, SearchMode mode, SearchScopeState? scope = null)
    {
        var result = await ExecuteAsync(new PagedRequest { Search = new SearchRequest("profile", term, mode) }, scope);

        return [.. result.Items.Select(i => i.Item.Id)];
    }

    private async Task<PagedResult<RankedItem<Candidate>>> ExecuteAsync(PagedRequest request, SearchScopeState? scope = null)
    {
        var executor = new SearchExecutor<Candidate, CandidateDto>(
            new CompiledSearchConfiguration<Candidate, CandidateDto>(new CandidateQueryConfiguration()),
            new NullTelemetry(),
            new SqliteSearchProvider(Options.Create(SqliteSearchProviderTests.SearchOptions())),
            new FixedScopeProvider(scope ?? new SearchScopeState("t1", [_berlin, _hamburg])));

        return await executor.SearchAsync(_context.Candidates, request);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
