using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>The catalog module context, over its own schema of the shared database.</summary>
public sealed class CatalogContext(DbContextOptions<CatalogContext> options) : DbContext(options)
{
    public DbSet<CatalogItem> Items => Set<CatalogItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.Entity<CatalogItem>(item =>
        {
            item.ToTable("items");
            item.Property(i => i.Id).HasColumnName("id");
            item.Property(i => i.Name).HasColumnName("name");
        });
    }
}
