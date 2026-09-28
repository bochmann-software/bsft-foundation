using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class BsftDbContextOptionsExtensionsTests
{
    [Fact]
    public void UseStaticTenantFilter_NullModelBuilder_ThrowsArgumentNullException()
    {
        ModelBuilder modelBuilder = null!;
        var tenantContext = new TenantContext();

        var act = () => modelBuilder.UseStaticTenantFilter(tenantContext);

        act.Should().Throw<ArgumentNullException>().WithParameterName("modelBuilder");
    }

    [Fact]
    public void UseStaticTenantFilter_NullTenantContext_ThrowsArgumentNullException()
    {
        var modelBuilder = new ModelBuilder();

        var act = () => modelBuilder.UseStaticTenantFilter(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantContext");
    }

    [Fact]
    public void UseTenantFilter_NullModelBuilderForScopedContext_ThrowsArgumentNullException()
    {
        ModelBuilder modelBuilder = null!;

        var act = () => modelBuilder.UseTenantFilter<TenantFilterDbContext>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("modelBuilder");
    }

    [Fact]
    public void UseTenantFilter_NullScopedContext_ThrowsArgumentNullException()
    {
        var modelBuilder = new ModelBuilder();

        var act = () => modelBuilder.UseTenantFilter<TenantFilterDbContext>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void UseTenantFilter_WithTenantEntity_AppliesFilter()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId("test-tenant");

        var options = new DbContextOptionsBuilder<TenantFilterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // Creating the context triggers OnModelCreating, which calls UseTenantFilter.
        using var context = new TenantFilterDbContext(options, tenantContext);
        context.Database.EnsureCreated();

        context.Model.FindEntityType(typeof(TestEntity))!.GetQueryFilter().Should().NotBeNull();
    }

    [Fact]
    public void UseStaticTenantFilter_WithMixedEntities_SkipsNonTenantEntity()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId("mixed-tenant");

        var options = new DbContextOptionsBuilder<MixedEntityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new MixedEntityDbContext(options, tenantContext);
        context.Database.EnsureCreated();

        context.Model.FindEntityType(typeof(TestEntity))!.GetQueryFilter().Should().NotBeNull();
        context.Model.FindEntityType(typeof(NonTenantEntity))!.GetQueryFilter().Should().BeNull();
    }

    /// <summary>
    /// Two tenants, one host: the second scope must see only its own rows. The model and the
    /// compiled-query cache are shared across scopes, so a filter that closes over the first scope's
    /// tenant value keeps filtering on it forever.
    /// </summary>
    [Fact]
    public async Task UseTenantFilter_AcrossScopes_FiltersOnEachScopesTenant()
    {
        var databaseName = Guid.NewGuid().ToString();

        await using var provider = BuildProvider(options => options.UseInMemoryDatabase(databaseName));

        await AssertEachScopeSeesItsOwnRowAsync(provider);
    }

    /// <summary>
    /// The same property on a relational provider, which is where the defect was reported: EF Core
    /// caches the SQL and the parameter values it extracted, so the tenant has to reach the query as a
    /// value read per execution rather than one baked into the cached plan.
    /// </summary>
    [Fact]
    public async Task UseTenantFilter_AcrossScopesOnARelationalProvider_FiltersOnEachScopesTenant()
    {
        // Held open for the test: an in-memory SQLite database lives exactly as long as its connection.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        await using var provider = BuildProvider(options => options.UseSqlite(connection));
        await CreateSchemaAsync(provider);

        await AssertEachScopeSeesItsOwnRowAsync(provider);
    }

    private static async Task AssertEachScopeSeesItsOwnRowAsync(IServiceProvider provider)
    {
        await SeedAsync(provider, "tenant-a", "row-a");
        await SeedAsync(provider, "tenant-b", "row-b");

        // Reading tenant a first is what puts a compiled query for this shape in the cache, so the
        // second read is the one that would inherit a baked-in tenant.
        var namesForA = await ReadNamesAsync(provider, "tenant-a");
        var namesForB = await ReadNamesAsync(provider, "tenant-b");

        namesForA.Should().Equal("row-a");
        namesForB.Should().Equal("row-b");
    }

    private static ServiceProvider BuildProvider(Action<DbContextOptionsBuilder> configureProvider)
    {
        return new ServiceCollection()
            .AddScoped<TenantContext>()
            .AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>())
            .AddDbContext<TenantFilterDbContext>(configureProvider)
            .BuildServiceProvider();
    }

    private static async Task CreateSchemaAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = ResolveForTenant(scope, "schema");

        await context.Database.EnsureCreatedAsync(CancellationToken.None);
    }

    private static async Task SeedAsync(IServiceProvider provider, string tenantId, string name)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = ResolveForTenant(scope, tenantId);

        context.TestEntities.Add(new TestEntity { Name = name, TenantId = tenantId });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<List<string>> ReadNamesAsync(IServiceProvider provider, string tenantId)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = ResolveForTenant(scope, tenantId);

        return await context.TestEntities
            .AsNoTracking()
            .Select(e => e.Name)
            .ToListAsync(CancellationToken.None);
    }

    private static TenantFilterDbContext ResolveForTenant(AsyncServiceScope scope, string tenantId)
    {
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId(tenantId);
        return scope.ServiceProvider.GetRequiredService<TenantFilterDbContext>();
    }
}
