namespace Bsft.EventAggregator.Saga.Persistence;

/// <summary>
/// The open persistence window of a single step. Everything the saga stores write while it is open
/// is staged; <see cref="CommitAsync"/> makes the whole set durable at once.
/// </summary>
public interface ISagaPersistenceWindowScope : IAsyncDisposable
{
    /// <summary>
    /// Commits everything staged in the window.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
