using Bsft.Data.Search;
using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Bsft.Data.Diagnostics;

namespace Bsft.Data.Tests.Search;

public class SearchExecutorTests : IDisposable
{
    private static readonly Guid _berlin = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _hamburg = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly CandidateContext _context;
    private readonly FakeSearchProvider _provider = new();
    private readonly RecordingTelemetry _telemetry = new();

    public SearchExecutorTests()
    {
        var options = new DbContextOptionsBuilder<CandidateContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CandidateContext(options);
        _context.Candidates.AddRange(
            new Candidate { Id = 1, Name = "Müller", Notes = "schweisser", Seniority = 20, TenantId = "t1", OrganizationUnitId = _berlin },
            new Candidate { Id = 2, Name = "Schmidt", Notes = "Müller as reference", Seniority = 5, TenantId = "t1", OrganizationUnitId = _berlin },
            new Candidate { Id = 3, Name = "Müller", Notes = null, Seniority = 10, TenantId = "t1", OrganizationUnitId = _hamburg },
            new Candidate { Id = 4, Name = "Müller", Notes = null, Seniority = 20, TenantId = "t1", OrganizationUnitId = _berlin },
            new Candidate { Id = 5, Name = "Müller", Notes = null, Seniority = 30, TenantId = "t2", OrganizationUnitId = _berlin });
        _context.SaveChanges();
    }

    [Fact]
    public async Task ResultsAreOrderedByRelevanceWithADeterministicTieBreaker()
    {
        var result = await ExecuteAsync(new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        result.Items.Select(i => i.Item.Id).Should().Equal(1, 3, 4, 2);
        result.Items[0].Score.Should().Be(2.0);
        result.Items[^1].Score.Should().Be(1.0);
    }

    [Fact]
    public async Task RankOrderingIsStableAcrossPages()
    {
        var first = await ExecuteAsync(new PagedRequest(Page: 1, PageSize: 2) { Search = new SearchRequest("profile", "Müller") });
        var second = await ExecuteAsync(new PagedRequest(Page: 2, PageSize: 2) { Search = new SearchRequest("profile", "Müller") });

        first.Items.Select(i => i.Item.Id).Should().Equal(1, 3);
        second.Items.Select(i => i.Item.Id).Should().Equal(4, 2);
        first.TotalCount.Should().Be(4);
        first.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task SearchComposesWithAStructuredFilter()
    {
        var filter = new ComparisonNode
        {
            Field = nameof(CandidateDto.Name),
            Op = FilterOperator.Eq,
            Value = System.Text.Json.JsonSerializer.SerializeToElement("Schmidt"),
        };

        var result = await ExecuteAsync(new PagedRequest(filter) { Search = new SearchRequest("profile", "Müller") });

        result.Items.Should().ContainSingle().Which.Item.Id.Should().Be(2);
    }

    [Fact]
    public async Task WithoutRankOrdering_TheRequestedSortApplies()
    {
        var request = new PagedRequest(Sort: [new SortItem(nameof(CandidateDto.Id), SortDirection.Desc)])
        {
            Search = new SearchRequest("profile", "Müller", IsOrderedByRank: false),
        };

        var result = await ExecuteAsync(request);

        result.Items.Select(i => i.Item.Id).Should().Equal(4, 3, 2, 1);
    }

    [Fact]
    public async Task WithoutRankOrderingAndWithoutASortTheConfiguredDefaultSortApplies()
    {
        var executor = CreateExecutor(new DescendingDefaultSortConfiguration(), null);

        var result = await executor.SearchAsync(
            _context.Candidates,
            new PagedRequest { Search = new SearchRequest("profile", "Müller", IsOrderedByRank: false) });

        result.Items.Select(i => i.Item.Id).Should().Equal(4, 3, 2, 1);
    }

    [Fact]
    public async Task WithoutRankOrderingAndWithAnEmptySortListTheConfiguredDefaultSortApplies()
    {
        // An empty list states no order, exactly like an absent one.
        var executor = CreateExecutor(new DescendingDefaultSortConfiguration(), null);

        var result = await executor.SearchAsync(
            _context.Candidates,
            new PagedRequest(Sort: []) { Search = new SearchRequest("profile", "Müller", IsOrderedByRank: false) });

        result.Items.Select(i => i.Item.Id).Should().Equal(4, 3, 2, 1);
    }

    [Fact]
    public async Task WithoutRankOrderingAndWithoutASortThePagesStayDisjointAndComplete()
    {
        // Skip/Take over an unordered source is decided by the query plan, so the same row can come
        // back on two pages and another on none. The default sort is what makes paging answerable.
        var executor = CreateExecutor(new DescendingDefaultSortConfiguration(), null);
        var search = new SearchRequest("profile", "Müller", IsOrderedByRank: false);

        var first = await executor.SearchAsync(_context.Candidates, new PagedRequest(Page: 1, PageSize: 2) { Search = search });
        var second = await executor.SearchAsync(_context.Candidates, new PagedRequest(Page: 2, PageSize: 2) { Search = search });

        first.Items.Select(i => i.Item.Id).Should().Equal(4, 3);
        second.Items.Select(i => i.Item.Id).Should().Equal(2, 1);
        first.Items.Select(i => i.Item.Id).Should().NotIntersectWith(second.Items.Select(i => i.Item.Id));
        first.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task WithoutRankOrderingAndWithoutAConfiguredDefaultSortTheEntityKeyOrders()
    {
        var executor = CreateExecutor(new NoDefaultSortConfiguration(), null);

        var result = await executor.SearchAsync(
            _context.Candidates,
            new PagedRequest { Search = new SearchRequest("profile", "Müller", IsOrderedByRank: false) });

        // Neither a requested sort nor a configured default is still no reason to hand Skip/Take an
        // unordered source. The fallback is now the entity's own key — the same unique tie-breaker the
        // query path uses — so rows tied on the non-unique search field still page in a fixed order.
        result.Items.Select(i => i.Item.Id).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task WithoutRankOrderingAndWithoutAConfiguredDefaultSortThePagesStayDisjoint()
    {
        var executor = CreateExecutor(new NoDefaultSortConfiguration(), null);
        var search = new SearchRequest("profile", "Müller", IsOrderedByRank: false);

        var first = await executor.SearchAsync(_context.Candidates, new PagedRequest(Page: 1, PageSize: 2) { Search = search });
        var second = await executor.SearchAsync(_context.Candidates, new PagedRequest(Page: 2, PageSize: 2) { Search = search });

        // Every matching row carries the same name, so only the key tie-breaker keeps the two pages
        // both disjoint and complete instead of the provider's arbitrary split.
        first.Items.Select(i => i.Item.Id).Should().Equal(1, 2);
        second.Items.Select(i => i.Item.Id).Should().Equal(3, 4);
        first.Items.Select(i => i.Item.Id).Should().NotIntersectWith(second.Items.Select(i => i.Item.Id));
        first.Items.Concat(second.Items).Select(i => i.Item.Id).Should().BeEquivalentTo([1, 2, 3, 4]);
    }

    [Fact]
    public async Task ForeignTenantRowsAreNeverReturned()
    {
        var result = await ExecuteAsync(
            new PagedRequest { Search = new SearchRequest("profile", "Müller") },
            new SearchScopeState("t2", [_berlin, _hamburg]));

        result.Items.Select(i => i.Item.Id).Should().Equal(5);
    }

    [Fact]
    public async Task ForeignOrgUnitRowsAreNeverReturned()
    {
        var result = await ExecuteAsync(
            new PagedRequest { Search = new SearchRequest("profile", "Müller") },
            new SearchScopeState("t1", [_hamburg]));

        result.Items.Select(i => i.Item.Id).Should().Equal(3);
    }

    [Fact]
    public async Task AnEmptyOrgScopeReturnsNothing()
    {
        var result = await ExecuteAsync(
            new PagedRequest { Search = new SearchRequest("profile", "Müller") },
            new SearchScopeState("t1", []));

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ThePageSizeIsCappedByTheConfiguration()
    {
        var result = await ExecuteAsync(new PagedRequest(PageSize: 5000) { Search = new SearchRequest("profile", "Müller") });

        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task DurationAndResultCountAreReported()
    {
        await ExecuteAsync(new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        _telemetry.Calls.Should().ContainSingle();
        _telemetry.Calls[0].Group.Should().Be("profile");
        _telemetry.Calls[0].ResultCount.Should().Be(4);
    }

    [Fact]
    public async Task ARequestWithoutAModeInheritsTheGroupDefault()
    {
        // Resolved once here rather than in every provider, which would otherwise each reimplement
        // the same fallback and could answer a different question than the one that was asked.
        await ExecuteAsync(new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        _provider.LastQuery!.Mode.Should().Be(SearchMode.FreeText);
        _provider.LastQuery.Group.Name.Should().Be("profile");
    }

    [Fact]
    public async Task AnExplicitModeOverridesTheGroupDefault()
    {
        await ExecuteAsync(new PagedRequest { Search = new SearchRequest("profile", "Müller", SearchMode.Prefix) });

        _provider.LastQuery!.Mode.Should().Be(SearchMode.Prefix);
    }

    [Fact]
    public async Task ARequestWithoutASearchIsRejected()
    {
        var act = async () => await ExecuteAsync(new PagedRequest());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no search*");
    }

    [Fact]
    public async Task AnUndeclaredGroupIsRejected()
    {
        var act = async () => await ExecuteAsync(new PagedRequest { Search = new SearchRequest("nope", "Müller") });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nope*");
    }

    [Fact]
    public async Task ArgumentsAreValidated()
    {
        var executor = CreateExecutor(null);

        await FluentActions.Awaiting(() => executor.SearchAsync(null!, new PagedRequest()))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("query");
        await FluentActions.Awaiting(() => executor.SearchAsync(_context.Candidates, null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public void ConstructorDependenciesAreValidated()
    {
        var configuration = new CompiledSearchConfiguration<Candidate, CandidateDto>(new CandidateQueryConfiguration());

        FluentActions.Invoking(() => new CompiledSearchConfiguration<Candidate, CandidateDto>(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");

        FluentActions.Invoking(() => new SearchExecutor<Candidate, CandidateDto>(null!, _telemetry, _provider))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        FluentActions.Invoking(() => new SearchExecutor<Candidate, CandidateDto>(configuration, null!, _provider))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
    }

    [Fact]
    public async Task ADeclaredGroupWithoutASearchProviderRefusesToSearch()
    {
        // The second lock behind SearchStartupValidator: the provider is optional so that an
        // application declaring no group needs none, and this is what keeps that optionality from
        // turning into a null reference should the refusal ever be reached.
        var executor = new SearchExecutor<Candidate, CandidateDto>(
            new CompiledSearchConfiguration<Candidate, CandidateDto>(new CandidateQueryConfiguration()),
            _telemetry,
            searchProvider: null,
            new FixedScopeProvider(FullScope));

        var act = async () => await executor.SearchAsync(
            _context.Candidates, new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ISearchProvider*");
    }

    [Fact]
    public async Task WithoutADefaultSortTheEntityKeyBreaksRankTies()
    {
        var executor = CreateExecutor(new NoDefaultSortConfiguration(), null);

        var result = await executor.SearchAsync(_context.Candidates, new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        // No configured default sort, but Candidate carries a key, so the ranked path breaks a score
        // tie on that key rather than on the non-unique first search field: the three score-2 Müller
        // rows keep ascending key order ahead of the single score-1 Schmidt.
        result.Items.Select(i => i.Item.Id).Should().Equal(1, 3, 4, 2);
    }

    [Fact]
    public async Task WithANonKeyDefaultSort_TheRankedPathOrdersByRankThenDefaultSortThenKey()
    {
        var executor = CreateExecutor(new SeniorityDefaultSortConfiguration(), null);

        var result = await executor.SearchAsync(_context.Candidates, new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        // Rank comes first: the three score-2 Müller rows (1, 3, 4) precede the single score-1 Schmidt
        // (2), even though 2 has the lowest seniority. Within the score-2 tie the configured non-key
        // DefaultSort on Seniority orders next — 3 (10) ahead of 1 and 4 (both 20) — and the entity key
        // is the final unique tie-breaker beneath it, keeping 1 ahead of 4. So a non-key DefaultSort is
        // honored as the secondary order while paging stays deterministic on the key.
        result.Items.Select(i => i.Item.Id).Should().Equal(3, 1, 4, 2);
    }

    [Fact]
    public async Task AScopedGroupWithoutAScopeProviderRefusesToSearch()
    {
        var executor = CreateUnscopedProviderExecutor(new CandidateQueryConfiguration());

        var act = async () => await executor.SearchAsync(
            _context.Candidates, new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ISearchScopeProvider*");
    }

    [Fact]
    public async Task AnExplicitlyUnscopedGroupSearchesWithoutAScopeProvider()
    {
        var executor = CreateUnscopedProviderExecutor(new UnscopedQueryConfiguration());

        var result = await executor.SearchAsync(
            _context.Candidates, new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        // Without a provider there is no boundary to resolve, so the group is searched whole. On a
        // marked entity like Candidate the declaration itself is refused at startup; what this pins
        // is that an unscoped group does not trip the missing-provider refusal.
        result.Items.Should().HaveCount(5, "an unscoped group needs no scope provider to resolve");
    }

    [Fact]
    public async Task AnUnscopedDeclarationOnAScopedEntityIsStillScopedAtRuntime()
    {
        // Candidate carries both markers, so this declaration is one startup validation refuses. If
        // it reaches the runtime anyway, the boundary stays on: the flag must never be able to switch
        // off tenant isolation for data that has a tenant.
        var executor = CreateExecutor(new UnscopedQueryConfiguration(), new SearchScopeState("t2", [_berlin, _hamburg]));

        var result = await executor.SearchAsync(
            _context.Candidates, new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        result.Items.Select(i => i.Item.Id).Should().Equal(5);
    }

    [Fact]
    public async Task AScopeThatLeavesTheOrgDimensionUnsetIsRejected()
    {
        var act = async () => await ExecuteAsync(
            new PagedRequest { Search = new SearchRequest("profile", "Müller") },
            new SearchScopeState("t1", null));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AllowedOrganizationUnitIds*");
    }

    [Fact]
    public async Task AScopeThatLeavesTheTenantDimensionUnsetIsRejected()
    {
        var act = async () => await ExecuteAsync(
            new PagedRequest { Search = new SearchRequest("profile", "Müller") },
            new SearchScopeState(null, [_berlin]));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*TenantId*");
    }

    [Fact]
    public void TheDeclaredGroupsAreExposedForStartupValidation()
    {
        ISearchGroupSource source = new CompiledSearchConfiguration<Candidate, CandidateDto>(new CandidateQueryConfiguration());

        var group = source.Groups.Should().ContainSingle().Subject;
        group.Name.Should().Be("profile");
        group.Language.Should().Be("de");
        group.DefaultMode.Should().Be(SearchMode.FreeText);
        group.Fields.Select(f => f.Name).Should().Equal("Name", "Notes");
        group.Fields[0].Weight.Should().Be(2.0);
    }

    private Task<PagedResult<RankedItem<Candidate>>> ExecuteAsync(PagedRequest request, SearchScopeState? scope = null) =>
        CreateExecutor(scope).SearchAsync(_context.Candidates, request);

    private SearchExecutor<Candidate, CandidateDto> CreateExecutor(SearchScopeState? scope) =>
        CreateExecutor(new CandidateQueryConfiguration(), scope);

    private SearchExecutor<Candidate, CandidateDto> CreateExecutor(
        QueryConfigurationBase<Candidate, CandidateDto> configuration,
        SearchScopeState? scope) =>
        new(new CompiledSearchConfiguration<Candidate, CandidateDto>(configuration),
            _telemetry,
            _provider,
            // A scoped group without a registered provider fails closed, so the tests that are not
            // about scoping pass one that restricts nothing.
            new FixedScopeProvider(scope ?? FullScope));

    /// <summary>
    /// The Candidate entity carries both scoping markers, so a scope has to name both dimensions —
    /// leaving one unset is now an error, not an implicit "all".
    /// </summary>
    private static SearchScopeState FullScope { get; } = new("t1", [_berlin, _hamburg]);

    /// <summary>An executor for an application that registered no scope provider at all.</summary>
    private SearchExecutor<Candidate, CandidateDto> CreateUnscopedProviderExecutor(
        QueryConfigurationBase<Candidate, CandidateDto> configuration) =>
        new(new CompiledSearchConfiguration<Candidate, CandidateDto>(configuration), _telemetry, _provider);

    private sealed class UnscopedQueryConfiguration : QueryConfigurationBase<Candidate, CandidateDto>
    {
        public override void Configure(QueryConfigurationBuilder<Candidate, CandidateDto> builder) =>
            builder.Searchable("profile", s => s.Field(e => e.Name).Field(e => e.Notes).Unscoped());
    }

    /// <summary>
    /// A descending default sort, so the fallback ordering is distinguishable from the order the
    /// source happens to return rows in.
    /// </summary>
    private sealed class DescendingDefaultSortConfiguration : QueryConfigurationBase<Candidate, CandidateDto>
    {
        public override void Configure(QueryConfigurationBuilder<Candidate, CandidateDto> builder)
        {
            builder.Field(d => d.Id).AllowSort();
            builder.DefaultSort(d => d.Id, SortDirection.Desc);
            builder.Searchable("profile", s => s.Field(e => e.Name).Field(e => e.Notes));
        }
    }

    /// <summary>
    /// A default sort on a non-key field, so the ranked path can be shown to order score ties by the
    /// configured field before falling back to the entity key.
    /// </summary>
    private sealed class SeniorityDefaultSortConfiguration : QueryConfigurationBase<Candidate, CandidateDto>
    {
        public override void Configure(QueryConfigurationBuilder<Candidate, CandidateDto> builder)
        {
            builder.Field(d => d.Seniority).AllowSort();
            builder.DefaultSort(d => d.Seniority, SortDirection.Asc);
            builder.Searchable("profile", s => s.Field(e => e.Name).Field(e => e.Notes));
        }
    }

    private sealed class NoDefaultSortConfiguration : QueryConfigurationBase<Candidate, CandidateDto>
    {
        public override void Configure(QueryConfigurationBuilder<Candidate, CandidateDto> builder) =>
            builder.Searchable("profile", s => s.Field(e => e.Name).Field(e => e.Notes));
    }

    private sealed class RecordingTelemetry : IDataTelemetry
    {
        public List<(string Group, double Seconds, int ResultCount)> Calls { get; } = [];

        public void SearchCompleted(string group, double seconds, int resultCount) =>
            Calls.Add((group, seconds, resultCount));
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
