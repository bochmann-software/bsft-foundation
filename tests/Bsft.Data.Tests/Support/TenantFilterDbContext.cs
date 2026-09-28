using Bsft.Data.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>
/// A test DbContext that calls UseTenantFilter in OnModelCreating to exercise
/// BsftDbContextOptionsExtensions.UseTenantFilter with a real ITenantEntity, through the
/// context-scoped overload.
/// </summary>
public sealed class TenantFilterDbContext : DbContext, ITenantScopedDbContext
{
    private readonly ITenantContext _tenantContext;

    public TenantFilterDbContext(DbContextOptions<TenantFilterDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    public string? CurrentTenantId => _tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>().HasKey(e => e.Id);
        modelBuilder.UseTenantFilter(this);
    }
}
