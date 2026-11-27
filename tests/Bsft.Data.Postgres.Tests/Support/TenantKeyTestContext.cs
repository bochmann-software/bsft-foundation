using Bsft.Data.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Test context with UseTenantKeys configured for tenant-keyed tables on PostgreSQL.</summary>
internal sealed class TenantKeyTestContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="TenantKeyTestContext"/> class.</summary>
    /// <param name="options">The context options.</param>
    public TenantKeyTestContext(DbContextOptions<TenantKeyTestContext> options) : base(options)
    {
    }

    /// <summary>Gets the tenant-keyed entities set.</summary>
    public DbSet<TenantKeyedTestEntity> TenantKeyedEntities => Set<TenantKeyedTestEntity>();

    /// <summary>Configures the model.</summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseTenantKeys();

        modelBuilder.Entity<TenantKeyedTestEntity>(builder =>
        {
            builder.ToTable("tenantKeyedEntities");
            builder.HasTenantKey(e => e.TenantId, e => e.Id);
        });
    }
}
