using Bsft.Messaging.EFCore.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>
/// A context that ends the sweep by a cancellation nobody asked for, which is how a command timeout
/// reaches the prune loop.
/// </summary>
public sealed class SelfCancelingInboxDbContext : IInboxDbContext
{
    public DbSet<InboxMessageEntity> InboxMessages => throw new OperationCanceledException();

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
}
