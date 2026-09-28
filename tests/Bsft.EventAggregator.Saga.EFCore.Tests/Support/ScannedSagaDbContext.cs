using Microsoft.EntityFrameworkCore;

namespace Bsft.EventAggregator.Saga.EFCore.Tests.Support;

/// <summary>
/// The same model reached by scanning the package assembly rather than naming each configuration.
/// It is a separate type because EF caches one model per context type.
/// </summary>
public class ScannedSagaDbContext : DbContext, ISagaTimeoutDbContext
{
    public ScannedSagaDbContext(DbContextOptions<ScannedSagaDbContext> options) : base(options) { }

    public DbSet<SagaStateEntity> SagaStates => Set<SagaStateEntity>();

    public DbSet<SagaTimeoutEntity> SagaTimeouts => Set<SagaTimeoutEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SagaTimeoutEntityConfiguration).Assembly);
    }
}
