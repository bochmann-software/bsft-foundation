using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore.Tests.Support;

/// <summary>
/// The same model reached the way the package advertises it: by scanning the package assembly rather
/// than naming the configuration. It is a separate type because EF caches one model per context type.
/// </summary>
public sealed class ScannedJobDbContext : DbContext, IJobDbContext
{
    public ScannedJobDbContext(DbContextOptions<ScannedJobDbContext> options) : base(options) { }

    public DbSet<JobRunEntity> JobRuns => Set<JobRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobRunEntityConfiguration).Assembly);
    }
}
