using Bsft.Data.Filtering;
using Bsft.Data.Search;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Bsft.Data.DependencyInjection;

namespace Bsft.Data.Tests.Search;

/// <summary>
/// The scope provider has to run in the caller's request scope. A provider derives the boundary from
/// request state — the ambient <see cref="ITenantContext"/> above all — and a scope opened by the
/// executor itself carries none of it: the tenant would read as null and every scoped search would
/// fail with a misleading "left TenantId unset" message.
/// </summary>
public sealed class SearchScopeResolutionTests : IDisposable
{
    private static readonly Guid _berlin = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _hamburg = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly CandidateContext _context;

    public SearchScopeResolutionTests()
    {
        var options = new DbContextOptionsBuilder<CandidateContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CandidateContext(options);
        _context.Candidates.AddRange(
            new Candidate { Id = 1, Name = "Müller", TenantId = "t1", OrganizationUnitId = _berlin },
            new Candidate { Id = 2, Name = "Müller", TenantId = "t1", OrganizationUnitId = _hamburg },
            new Candidate { Id = 5, Name = "Müller", TenantId = "t2", OrganizationUnitId = _berlin });
        _context.SaveChanges();
    }

    [Fact]
    public async Task TheScopeProviderSeesTheTenantContextOfTheCallersRequestScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Exactly what TenantMiddleware does at the start of a request.
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId("t2");

        var executor = scope.ServiceProvider.GetRequiredService<ISearchExecutor<Candidate, CandidateDto>>();
        var result = await executor.SearchAsync(
            _context.Candidates,
            new PagedRequest { Search = new SearchRequest("profile", "Müller") });

        result.Items.Select(i => i.Item.Id).Should().Equal(5);
    }

    [Fact]
    public async Task TwoRequestScopesEachSeeTheirOwnTenant()
    {
        using var provider = BuildProvider();
        var request = new PagedRequest { Search = new SearchRequest("profile", "Müller") };

        using var first = provider.CreateScope();
        first.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId("t1");
        var firstResult = await first.ServiceProvider
            .GetRequiredService<ISearchExecutor<Candidate, CandidateDto>>()
            .SearchAsync(_context.Candidates, request);

        using var second = provider.CreateScope();
        second.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId("t2");
        var secondResult = await second.ServiceProvider
            .GetRequiredService<ISearchExecutor<Candidate, CandidateDto>>()
            .SearchAsync(_context.Candidates, request);

        firstResult.Items.Select(i => i.Item.Id).Should().Equal(1, 2);
        secondResult.Items.Select(i => i.Item.Id).Should().Equal(5);
    }

    /// <summary>
    /// Validating scopes proves the executor's lifetime is honest: a singleton depending on the
    /// request-scoped provider would be refused here rather than quietly capturing one request.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISearchProvider, FakeSearchProvider>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ISearchScopeProvider, TenantSearchScopeProvider>();
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));

        // The scan finds several configurations for this entity/DTO pair; the last registration wins,
        // so the one under test is pinned rather than left to reflection order.
        services.AddSingleton<QueryConfigurationBase<Candidate, CandidateDto>, CandidateQueryConfiguration>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The scope provider an application writes: request state in, boundary out.</summary>
    private sealed class TenantSearchScopeProvider : ISearchScopeProvider
    {
        private readonly ITenantContext _tenantContext;

        public TenantSearchScopeProvider(ITenantContext tenantContext) =>
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));

        public Task<SearchScopeState> GetScopeAsync(CancellationToken ct = default) =>
            Task.FromResult(new SearchScopeState(_tenantContext.TenantId, [_berlin, _hamburg]));
    }

    public void Dispose() => _context.Dispose();
}
