using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// A PostgreSQL-configured context that is never connected: EF can still generate the SQL for a
/// query, which is what the provider tests assert against.
/// </summary>
/// <remarks>
/// Deliberately not sealed: the two contexts beside it derive from it to map the generated column
/// wrongly on purpose, which is how the provider's refusals are reached.
/// </remarks>
public class SearchTestContext : DbContext
{
    public DbSet<Candidate> Candidates => Set<Candidate>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql("Host=unused;Database=unused");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Candidate>().ToTable("Candidates");
        MapVector(modelBuilder);
    }

    /// <summary>Maps the generated column. Overridden by the contexts that deliberately get it wrong.</summary>
    protected virtual void MapVector(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Candidate>().Property<NpgsqlTsVector>(BsftDataPostgresOptions.DefaultVectorProperty);
        modelBuilder.Entity<Candidate>().HasIndex(BsftDataPostgresOptions.DefaultVectorProperty).HasMethod("GIN");
    }
}
