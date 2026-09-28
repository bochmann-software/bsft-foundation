using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore.Tests.Support;

/// <summary>
/// A consuming application's DbContext as the EF Core job store expects it: the entity configuration
/// applied with its default filtered unique index.
/// </summary>
public sealed class TestJobDbContext : DbContext, IJobDbContext
{
    public TestJobDbContext(DbContextOptions<TestJobDbContext> options) : base(options) { }

    public DbSet<JobRunEntity> JobRuns => Set<JobRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new JobRunEntityConfiguration());
    }
}
