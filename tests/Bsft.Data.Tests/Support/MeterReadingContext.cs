using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>The context of the keyset paging tests, on SQLite and on the in-memory provider.</summary>
public sealed class MeterReadingContext : DbContext
{
    public MeterReadingContext(DbContextOptions<MeterReadingContext> options) : base(options)
    {
    }

    public DbSet<MeterReading> Readings => Set<MeterReading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<MeterReading>().HasKey(e => e.Id);
}
