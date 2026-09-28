using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Inbox;

/// <summary>
/// Contract for a DbContext that provides the integration event inbox. The consuming application's
/// DbContext must implement it to use the EF Core inbox.
/// </summary>
/// <remarks>
/// It is the same arrangement as <see cref="Outbox.IOutboxDbContext"/> and for the same reason, one step
/// further along: because the inbox lives in the application's own context, the record that an event
/// was handled is written by the same <c>SaveChanges</c> as the changes handling it produced — and if
/// those changes stage new integration events, the outbox rows for them go into that transaction too.
/// One commit then covers the whole of "we processed this event, here is what it changed, and here is
/// what we are telling the world about it".
/// </remarks>
public interface IInboxDbContext
{
    /// <summary>
    /// Gets the set of processed inbox records.
    /// </summary>
    DbSet<InboxMessageEntity> InboxMessages { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
