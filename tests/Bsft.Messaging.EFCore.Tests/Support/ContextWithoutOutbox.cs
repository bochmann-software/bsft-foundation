using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Tests.Support;

/// <summary>A context without the outbox, used to prove the interceptor rejects a wrong host.</summary>
public class ContextWithoutOutbox : DbContext
{
    public ContextWithoutOutbox(DbContextOptions<ContextWithoutOutbox> options) : base(options) { }

    public DbSet<OrderRow> Orders => Set<OrderRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OrderRow>().HasKey(e => e.Id);
}
