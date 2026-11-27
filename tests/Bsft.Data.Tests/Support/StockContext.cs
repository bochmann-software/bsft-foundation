using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>The second of two module contexts over one database, as a modular host has them.</summary>
public sealed class StockContext : DbContext
{
    public StockContext(DbContextOptions<StockContext> options) : base(options)
    {
    }

    public DbSet<StockItem> Items => Set<StockItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StockItem>().ToTable("StockItems");
}
