using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.SqlServer.Tests.Support;

/// <summary>
/// A SQL Server-configured context that is never connected: EF can still generate the SQL for a
/// query, which is what the provider tests assert against.
/// </summary>
public sealed class SearchTestContext : DbContext
{
    public DbSet<Candidate> Candidates => Set<Candidate>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;");
}
