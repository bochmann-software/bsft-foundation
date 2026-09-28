using Bsft.Messaging.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Postgres.Tests.Support;

/// <summary>
/// The application's context, as far as this package is concerned: it holds the outbox and nothing
/// else. It runs on SQLite, which is enough for everything under test here — the interceptor reads the
/// change tracker, and the notifier is asserted by the statement it sends rather than by a server
/// acting on it.
/// </summary>
public sealed class TestOutboxDbContext : DbContext, IOutboxDbContext
{
    public TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options)
        : base(options)
    {
    }

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    Task<int> IOutboxDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityConfiguration());
}
