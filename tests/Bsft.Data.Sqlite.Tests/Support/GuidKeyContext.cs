using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>An application whose searchable entity is keyed on a GUID.</summary>
public sealed class GuidKeyContext : DbContext
{
    public DbSet<Referral> Referrals => Set<Referral>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite("Data Source=unused");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Referral>().ToTable("Referrals");
        modelBuilder.HasBsftFullTextIndex("Candidates_fts", "Name", "Notes");
    }
}
