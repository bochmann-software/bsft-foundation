using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>The inventory module context, over its own schema of the shared database.</summary>
public sealed class InventoryContext(DbContextOptions<InventoryContext> options) : DbContext(options)
{
    public DbSet<StockLevel> Stock => Set<StockLevel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        modelBuilder.Entity<StockLevel>(stock =>
        {
            stock.ToTable("stock");
            stock.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            stock.Property(s => s.Sku).HasColumnName("sku");
            stock.Property(s => s.Quantity).HasColumnName("quantity");
        });
    }
}
