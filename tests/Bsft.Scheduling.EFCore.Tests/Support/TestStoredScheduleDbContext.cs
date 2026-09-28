using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore.Tests.Support;

/// <summary>
/// A consuming application's DbContext as the EF Core stored-schedule store expects it: the entity
/// configuration applied with its indexes.
/// </summary>
public sealed class TestStoredScheduleDbContext : DbContext, IStoredScheduleDbContext
{
    public TestStoredScheduleDbContext(DbContextOptions<TestStoredScheduleDbContext> options) : base(options) { }

    public DbSet<StoredScheduleEntity> StoredSchedules => Set<StoredScheduleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new StoredScheduleEntityConfiguration());
    }
}
