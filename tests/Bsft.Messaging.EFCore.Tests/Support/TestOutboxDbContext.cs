using Bsft.EventAggregator.Saga.EFCore;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Tests.Support;

/// <summary>
/// The application's context: it carries the business tables, the saga tables and the outbox, which
/// is what lets one SaveChanges commit a state change together with the events it raises.
/// </summary>
public class TestOutboxDbContext : DbContext, IOutboxDbContext, IInboxDbContext, ISagaTimeoutDbContext
{
    public TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options) : base(options) { }

    public DbSet<OrderRow> Orders => Set<OrderRow>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    public DbSet<SagaStateEntity> SagaStates => Set<SagaStateEntity>();

    public DbSet<SagaTimeoutEntity> SagaTimeouts => Set<SagaTimeoutEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SagaStateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SagaTimeoutEntityConfiguration());
        modelBuilder.Entity<OrderRow>().HasKey(e => e.Id);
    }
}
