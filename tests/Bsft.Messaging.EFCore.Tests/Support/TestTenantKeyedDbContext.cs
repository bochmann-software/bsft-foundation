using Bsft.Data.MultiTenancy;
using Bsft.EventAggregator.Saga.EFCore;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Tests.Support;

/// <summary>
/// A test DbContext configured with tenant keys enabled for OutboxMessages and InboxMessages.
/// </summary>
public class TestTenantKeyedDbContext : DbContext, IOutboxDbContext, IInboxDbContext, ISagaTimeoutDbContext
{
    public TestTenantKeyedDbContext(DbContextOptions<TestTenantKeyedDbContext> options) : base(options) { }

    public DbSet<OrderRow> Orders => Set<OrderRow>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    public DbSet<SagaStateEntity> SagaStates => Set<SagaStateEntity>();

    public DbSet<SagaTimeoutEntity> SagaTimeouts => Set<SagaTimeoutEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enable tenant keys BEFORE applying framework entity configurations
        modelBuilder.UseTenantKeys();

        modelBuilder.ApplyConfiguration(new OutboxMessageEntityConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SagaStateEntityConfiguration());
        // Postgres requires double-quoted identifiers, not SQL Server brackets
        modelBuilder.ApplyConfiguration(new SagaTimeoutEntityConfiguration("\"DispatchedAt\" IS NULL"));
        modelBuilder.Entity<OrderRow>().HasKey(e => e.Id);
    }
}
