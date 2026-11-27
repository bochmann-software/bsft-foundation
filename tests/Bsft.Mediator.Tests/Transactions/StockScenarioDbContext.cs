using Microsoft.EntityFrameworkCore;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>The stock module's context in the shared-transaction scenarios; only a nested command uses it.</summary>
public sealed class StockScenarioDbContext(DbContextOptions<StockScenarioDbContext> options) : DbContext(options)
{
    public DbSet<ScenarioRow> Reservations => Set<ScenarioRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ScenarioRow>().ToTable("Reservations");
}
