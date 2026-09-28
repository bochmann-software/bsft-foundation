using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>An application whose searchable entity carries the key the index can be built on.</summary>
public class LongKeyContext : DbContext
{
    public DbSet<Applicant> Applicants => Set<Applicant>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite("Data Source=unused");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Map(modelBuilder.Entity<Applicant>());
        modelBuilder.HasBsftFullTextIndex("Candidates_fts", "Name", "Notes");
    }

    /// <summary>Shapes the key. Overridden by the contexts whose key cannot back an FTS5 rowid.</summary>
    /// <param name="entity">The entity being mapped.</param>
    protected virtual void Map(EntityTypeBuilder<Applicant> entity) => entity.ToTable("Applicants");
}
