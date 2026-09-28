using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore.Tests.Support;

/// <summary>
/// The same model without the recurring-occurrence uniqueness, used to prove that a null pending
/// filter really does drop the index. It is a separate type because EF caches one model per context
/// type.
/// </summary>
public sealed class UnfilteredJobDbContext : DbContext, IJobDbContext
{
    public UnfilteredJobDbContext(DbContextOptions<UnfilteredJobDbContext> options) : base(options) { }

    public DbSet<JobRunEntity> JobRuns => Set<JobRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new JobRunEntityConfiguration(pendingFilter: null));
    }
}
