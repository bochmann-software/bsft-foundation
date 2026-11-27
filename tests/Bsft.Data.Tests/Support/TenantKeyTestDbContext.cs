using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>Test context with UseTenantKeys enabled for tenant-keyed entities.</summary>
internal sealed class TenantKeyTestDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="TenantKeyTestDbContext"/> class.</summary>
    /// <param name="options">The context options.</param>
    public TenantKeyTestDbContext(DbContextOptions<TenantKeyTestDbContext> options) : base(options)
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
            if (builder.IsTenantKeyed())
            {
                builder.HasTenantKey(e => e.TenantId, e => e.Id);
            }
            else
            {
                builder.HasKey(e => e.Id);
            }
        });
    }
}
