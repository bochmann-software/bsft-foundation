namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// Commits one batch of items to wherever they end up: a database, a file, a remote endpoint.
/// </summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
/// <remarks>
/// A batch succeeds by returning and fails by throwing; the batching worker logs the failure, counts
/// the whole batch as failed and carries on with the next one, so a committer needs no catch-all of its
/// own. A committer that can tell one bad item from a bad batch handles the item itself.
/// </remarks>
public interface IBatchCommitter<in TItem>
{
    /// <summary>Commits <paramref name="batch"/>.</summary>
    /// <param name="batch">The items to commit; never empty.</param>
    /// <param name="cancellationToken">Cancelled when the stop deadline of the final flush passes.</param>
    /// <returns>A task that completes when the batch is committed.</returns>
    Task CommitAsync(IReadOnlyList<TItem> batch, CancellationToken cancellationToken);
}
