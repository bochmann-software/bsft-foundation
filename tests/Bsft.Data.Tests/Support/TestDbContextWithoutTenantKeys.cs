using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>Test context without UseTenantKeys.</summary>
internal sealed class TestDbContextWithoutTenantKeys : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="TestDbContextWithoutTenantKeys"/> class.</summary>
    /// <param name="options">The context options.</param>
    public TestDbContextWithoutTenantKeys(DbContextOptions<TestDbContextWithoutTenantKeys> options) : base(options)
    {
    }

    /// <summary>Gets the test entities set.</summary>
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    /// <summary>Configures the model.</summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Not calling UseTenantKeys - entities should use default configuration
        modelBuilder.Entity<TestEntity>(builder =>
        {
            builder.HasKey(e => e.Id);
        });
    }
}
