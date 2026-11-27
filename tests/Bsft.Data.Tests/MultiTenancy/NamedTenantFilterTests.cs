#if NET10_0_OR_GREATER
using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>
/// The tenant filter applied by name sits next to further named filters, and a query can step around
/// it alone.
/// </summary>
public sealed class NamedTenantFilterTests : IDisposable
{
    private const string HiddenFilterName = "hidden";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public NamedTenantFilterTests()
    {
        _connection.Open();

        using var context = Context("tenant-a", static (builder, owner) => builder.UseNamedTenantFilter(owner));
        context.Database.EnsureCreated();
        context.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "visible", TenantId = "tenant-a" },
            new TestEntity { Id = 2, Name = "hidden", TenantId = "tenant-a" },
            new TestEntity { Id = 3, Name = "visible", TenantId = "tenant-b" });
        context.SaveChanges();
    }

    [Fact]
    public void UseNamedTenantFilter_WithASecondNamedFilter_AppliesBoth()
    {
        using var context = Context("tenant-a", static (builder, owner) => builder.UseNamedTenantFilter(owner));

        context.TestEntities.Select(entity => entity.Id).Should().Equal(1);
        context.Model.FindEntityType(typeof(TestEntity))!.GetDeclaredQueryFilters()
            .Select(filter => filter.Key)
            .Should().BeEquivalentTo([BsftDbContextOptionsExtensions.TenantFilterName, HiddenFilterName]);
    }

    [Fact]
    public void UseNamedTenantFilter_IgnoringTheTenantFilterByName_KeepsTheOtherFilter()
    {
        using var context = Context("tenant-a", static (builder, owner) => builder.UseNamedTenantFilter(owner));

        var ids = context.TestEntities
            .IgnoreQueryFilters([BsftDbContextOptionsExtensions.TenantFilterName])
            .OrderBy(entity => entity.Id)
            .Select(entity => entity.Id)
            .ToList();

        ids.Should().Equal(1, 3);
    }

    [Fact]
    public void UseNamedTenantFilter_WithACustomName_NamesTheFilterSo()
    {
        using var context = Context("tenant-a", static (builder, owner) => builder.UseNamedTenantFilter(owner, "tenancy"));

        context.TestEntities
            .IgnoreQueryFilters(["tenancy"])
            .Count()
            .Should().Be(2);
    }

    [Fact]
    public void UseNamedStaticTenantFilter_WithASecondNamedFilter_AppliesBoth()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId("tenant-b");
        using var context = Context("tenant-b", (builder, _) => builder.UseNamedStaticTenantFilter(tenantContext, "static"));

        context.TestEntities.Select(entity => entity.Id).Should().Equal(3);
        context.TestEntities.IgnoreQueryFilters(["static"]).Count().Should().Be(2);
    }

    [Fact]
    public void UseNamedTenantFilter_WithAnInvalidArgument_Throws()
    {
        var modelBuilder = new ModelBuilder();
        using var context = Context("tenant-a", static (builder, owner) => builder.UseNamedTenantFilter(owner));

        FluentActions.Invoking(() => ((ModelBuilder)null!).UseNamedTenantFilter(context))
            .Should().Throw<ArgumentNullException>().WithParameterName("modelBuilder");
        FluentActions.Invoking(() => modelBuilder.UseNamedTenantFilter<NamedFilterDbContext>(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("context");
        FluentActions.Invoking(() => modelBuilder.UseNamedTenantFilter(context, " "))
            .Should().Throw<ArgumentException>().WithParameterName("filterName");
    }

    [Fact]
    public void UseNamedStaticTenantFilter_WithAnInvalidArgument_Throws()
    {
        var modelBuilder = new ModelBuilder();
        var tenantContext = new TenantContext();

        FluentActions.Invoking(() => ((ModelBuilder)null!).UseNamedStaticTenantFilter(tenantContext))
            .Should().Throw<ArgumentNullException>().WithParameterName("modelBuilder");
        FluentActions.Invoking(() => modelBuilder.UseNamedStaticTenantFilter(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("tenantContext");
        FluentActions.Invoking(() => modelBuilder.UseNamedStaticTenantFilter(tenantContext, ""))
            .Should().Throw<ArgumentException>().WithParameterName("filterName");
    }

    public void Dispose() => _connection.Dispose();

    private NamedFilterDbContext Context(string tenantId, Action<ModelBuilder, NamedFilterDbContext> applyTenantFilter)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var options = new DbContextOptionsBuilder<NamedFilterDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, PerCallModelCacheKeyFactory>()
            .Options;

        return new NamedFilterDbContext(options, tenantContext, applyTenantFilter);
    }

    /// <summary>A context whose tenant filter the test chooses, next to a named filter of its own.</summary>
    public sealed class NamedFilterDbContext(
        DbContextOptions<NamedFilterDbContext> options,
        ITenantContext tenantContext,
        Action<ModelBuilder, NamedFilterDbContext> applyTenantFilter)
        : DbContext(options), ITenantScopedDbContext
    {
        public DbSet<TestEntity> TestEntities => Set<TestEntity>();

        public string? CurrentTenantId => tenantContext.TenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>().HasKey(entity => entity.Id);
            modelBuilder.Entity<TestEntity>().HasQueryFilter(HiddenFilterName, entity => entity.Name != "hidden");
            applyTenantFilter(modelBuilder, this);
        }
    }

    /// <summary>Builds the model per context instance, so each test's filter choice takes effect.</summary>
    private sealed class PerCallModelCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => new object();
    }
}
#endif
