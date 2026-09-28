using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Idempotency.EFCore.Tests.Fakes;

/// <summary>
/// A context that ends the sweep by a cancellation nobody asked for, which is how a command timeout
/// reaches the prune loop.
/// </summary>
public sealed class SelfCancelingIdempotencyDbContext : IIdempotencyDbContext
{
    public DbSet<IdempotencyRecordEntity> IdempotencyRecords => throw new OperationCanceledException();

    public EntityEntry Entry(object entity) => throw new NotSupportedException();

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
}
