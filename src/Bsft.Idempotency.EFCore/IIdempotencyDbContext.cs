using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Bsft.Idempotency.EFCore;

/// <summary>
/// The one set the idempotency store writes through, on the application's own context.
/// </summary>
/// <remarks>
/// The application's context rather than one of this package's own, because that is what makes
/// <c>Enlisted</c> mode possible at all: a reservation staged in the same unit of work as the
/// business row commits with it, so a crash cannot leave one without the other.
/// </remarks>
public interface IIdempotencyDbContext
{
    /// <summary>Gets the idempotency records.</summary>
    DbSet<IdempotencyRecordEntity> IdempotencyRecords { get; }

    /// <summary>
    /// Gets the change-tracking entry for an entity, so the store can detach the row a refused
    /// insert staged instead of leaving it to be carried into a later save.
    /// </summary>
    /// <param name="entity">The entity to get the entry for.</param>
    /// <returns>The entry.</returns>
    /// <remarks>
    /// A <see cref="DbContext"/> exposes this already, so the application's context satisfies it
    /// without writing anything.
    /// </remarks>
    EntityEntry Entry(object entity);

    /// <summary>Saves the changes made in this unit of work.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
