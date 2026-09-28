using Microsoft.EntityFrameworkCore;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// Contract for a DbContext that provides saga state persistence.
/// The consuming application's DbContext must implement this interface
/// when using the EF Core saga store.
/// </summary>
public interface ISagaDbContext
{
    /// <summary>
    /// Gets the set of persisted saga state entities.
    /// </summary>
    DbSet<SagaStateEntity> SagaStates { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
