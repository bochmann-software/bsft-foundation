using Microsoft.EntityFrameworkCore;

namespace Bsft.EventAggregator.Saga.EFCore.Tests.Support;

public class TestSagaDbContext : DbContext, ISagaTimeoutDbContext
{
    public TestSagaDbContext(DbContextOptions<TestSagaDbContext> options) : base(options) { }

    public DbSet<SagaStateEntity> SagaStates => Set<SagaStateEntity>();

    public DbSet<SagaTimeoutEntity> SagaTimeouts => Set<SagaTimeoutEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SagaStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SagaTimeoutEntityConfiguration());
    }
}
