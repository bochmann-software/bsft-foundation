using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>The first of two module contexts over one database, as a modular host has them.</summary>
public sealed class CatalogContext : DbContext
{
    public CatalogContext(DbContextOptions<CatalogContext> options) : base(options)
    {
    }

    public DbSet<CatalogItem> Items => Set<CatalogItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<CatalogItem>().ToTable("CatalogItems");
}
