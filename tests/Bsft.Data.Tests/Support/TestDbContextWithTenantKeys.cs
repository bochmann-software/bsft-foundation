using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>Test context with UseTenantKeys enabled.</summary>
internal sealed class TestDbContextWithTenantKeys : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="TestDbContextWithTenantKeys"/> class.</summary>
    /// <param name="options">The context options.</param>
    public TestDbContextWithTenantKeys(DbContextOptions<TestDbContextWithTenantKeys> options) : base(options)
    {
    }

    /// <summary>Gets the tenant-keyed test entities set.</summary>
    public DbSet<TenantKeyedTestEntity> TenantKeyedTestEntities => Set<TenantKeyedTestEntity>();

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
