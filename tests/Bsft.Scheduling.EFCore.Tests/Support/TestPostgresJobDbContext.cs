using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore.Tests.Support;

/// <summary>
/// A consuming application's DbContext on PostgreSQL: the entity configuration applied with the
/// provider's own quoting for the pending-occurrence filter.
/// </summary>
public sealed class TestPostgresJobDbContext : DbContext, IJobDbContext
{
    public const string PostgresPendingFilter = "\"IsRecurring\" = true AND \"Status\" = 0";

    public TestPostgresJobDbContext(DbContextOptions<TestPostgresJobDbContext> options) : base(options) { }

    public DbSet<JobRunEntity> JobRuns => Set<JobRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new JobRunEntityConfiguration(PostgresPendingFilter));
    }
}
