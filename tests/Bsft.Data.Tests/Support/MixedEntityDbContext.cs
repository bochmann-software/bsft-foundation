using Bsft.Data.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>
/// A DbContext with both a tenant entity and a non-tenant entity, so that the tenant filter skips the
/// non-tenant one. It uses the fixed-tenant entry point, which keeps that one covered too.
/// </summary>
public sealed class MixedEntityDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public MixedEntityDbContext(DbContextOptions<MixedEntityDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    public DbSet<NonTenantEntity> NonTenantEntities => Set<NonTenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<NonTenantEntity>().HasKey(e => e.Id);
        modelBuilder.UseStaticTenantFilter(_tenantContext);
    }
}
