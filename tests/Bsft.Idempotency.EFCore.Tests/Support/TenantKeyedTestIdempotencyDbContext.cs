using Microsoft.EntityFrameworkCore;
using Bsft.Data.MultiTenancy;

namespace Bsft.Idempotency.EFCore.Tests.Support;

/// <summary>
/// The application's context with tenant keys enabled: its own business table and the idempotency table
/// together, where the idempotency table is tenant-keyed.
/// </summary>
public class TenantKeyedTestIdempotencyDbContext : DbContext, IIdempotencyDbContext
{
    public TenantKeyedTestIdempotencyDbContext(DbContextOptions<TenantKeyedTestIdempotencyDbContext> options)
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
        // Enable tenant keys on the entire model before applying configurations.
        modelBuilder.UseTenantKeys();

        modelBuilder.ApplyConfiguration(new IdempotencyRecordEntityConfiguration());
        modelBuilder.Entity<BusinessRecordEntity>().ToTable("BusinessRecords");
    }
}
