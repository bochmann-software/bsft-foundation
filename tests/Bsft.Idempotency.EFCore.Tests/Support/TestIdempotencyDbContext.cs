using Microsoft.EntityFrameworkCore;

namespace Bsft.Idempotency.EFCore.Tests.Support;

/// <summary>
/// The application's context: its own business table and the idempotency table together, which is the
/// whole reason this package writes through a context it does not own.
/// </summary>
public class TestIdempotencyDbContext : DbContext, IIdempotencyDbContext
{
    public TestIdempotencyDbContext(DbContextOptions<TestIdempotencyDbContext> options)
        : base(options)
    {
    }

    public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();

    /// <summary>
    /// The row an endpoint writes for real. It is what makes the mode contrast observable: enlisted, it
    /// commits with the reservation; detached, the reservation can exist without it.
    /// </summary>
    public DbSet<BusinessRecordEntity> BusinessRecords => Set<BusinessRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new IdempotencyRecordEntityConfiguration());
        modelBuilder.Entity<BusinessRecordEntity>().ToTable("BusinessRecords");
    }
}
