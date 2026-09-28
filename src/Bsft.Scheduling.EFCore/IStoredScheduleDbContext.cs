using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// Contract for a DbContext that persists the stored-schedule catalog. The consuming application's
/// DbContext must implement this interface when using the EF Core stored-schedule store.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="IJobDbContext"/> rather than adding a second <c>DbSet</c> to it: a
/// consumer that already implements <see cref="IJobDbContext"/> for delayed and recurring jobs would
/// otherwise be broken by a new required member the moment it upgrades, and a host that only wants the
/// job store has no reason to carry a table it never queries.
/// </remarks>
public interface IStoredScheduleDbContext
{
    /// <summary>Gets the set of persisted stored schedules.</summary>
    DbSet<StoredScheduleEntity> StoredSchedules { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
