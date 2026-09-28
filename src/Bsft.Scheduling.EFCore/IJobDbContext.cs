using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// Contract for a DbContext that persists job runs. The consuming application's DbContext must
/// implement this interface when using the EF Core job store.
/// </summary>
public interface IJobDbContext
{
    /// <summary>Gets the set of persisted job runs.</summary>
    DbSet<JobRunEntity> JobRuns { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
