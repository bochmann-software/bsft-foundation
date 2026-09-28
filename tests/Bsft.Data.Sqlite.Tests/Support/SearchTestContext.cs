using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>
/// A SQLite-configured context. Given no connection it is never connected — EF can still generate
/// the SQL for a query, which is what the provider tests assert against — and given one it runs
/// against a real database with a real FTS5 index.
/// </summary>
/// <remarks>
/// Deliberately not sealed: the two contexts beside it derive from it to map the index wrongly on
/// purpose, which is how the provider's refusals are reached.
/// </remarks>
public class SearchTestContext : DbContext
{
    private readonly SqliteConnection? _connection;

    public SearchTestContext()
    {
    }

    public SearchTestContext(SqliteConnection connection) => _connection = connection;

    public DbSet<Candidate> Candidates => Set<Candidate>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_connection is null)
            optionsBuilder.UseSqlite("Data Source=unused");
        else
            optionsBuilder.UseSqlite(_connection);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Candidate>().ToTable("Candidates");
        MapIndex(modelBuilder);
    }

    /// <summary>Maps the FTS5 index. Overridden by the contexts that deliberately get it wrong.</summary>
    protected virtual void MapIndex(ModelBuilder modelBuilder) =>
        modelBuilder.HasBsftFullTextIndex("Candidates_fts", "Name", "Notes");
}
