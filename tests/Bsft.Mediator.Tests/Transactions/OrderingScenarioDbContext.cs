using Microsoft.EntityFrameworkCore;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>The ordering module's context in the shared-transaction scenarios.</summary>
public sealed class OrderingScenarioDbContext(DbContextOptions<OrderingScenarioDbContext> options) : DbContext(options)
{
    public DbSet<ScenarioRow> Orders => Set<ScenarioRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ScenarioRow>().ToTable("Orders");
}
