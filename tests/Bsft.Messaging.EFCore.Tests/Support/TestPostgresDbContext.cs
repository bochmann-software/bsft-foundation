using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Tests.Support;

/// <summary>
/// The outbox and inbox tables without tenant keys, for PostgreSQL: the counter-check to
/// <see cref="TestTenantKeyedDbContext"/>.
/// </summary>
public class TestPostgresDbContext : DbContext, IOutboxDbContext, IInboxDbContext
{
    public TestPostgresDbContext(DbContextOptions<TestPostgresDbContext> options) : base(options) { }

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageEntityConfiguration());
    }
}
