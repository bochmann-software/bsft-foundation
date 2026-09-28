using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// Contract for a DbContext that provides the integration event outbox. The consuming application's
/// DbContext must implement it to use the EF Core outbox.
/// </summary>
/// <remarks>
/// The same DbContext type may implement further framework contracts — the saga state and timeout
/// contracts of <c>Bsft.EventAggregator.Saga.EFCore</c>, for instance. That is the whole point of
/// keeping the outbox in the application's own context: saga state and the integration events it
/// raises are then written by one <c>SaveChanges</c>, in one transaction, and cannot disagree.
/// </remarks>
public interface IOutboxDbContext
{
    /// <summary>
    /// Gets the set of pending and published outbox messages.
    /// </summary>
    DbSet<OutboxMessageEntity> OutboxMessages { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
