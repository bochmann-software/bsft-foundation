namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>Commits one batch through the pipeline's committer and gives up on it when told to.</summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
internal interface IBatchCommitRunner<TItem>
{
    /// <summary>Commits <paramref name="batch"/>, abandoning the commit when <paramref name="abort"/> is cancelled.</summary>
    /// <param name="batch">The items to commit; never empty.</param>
    /// <param name="abort">Gives up on a commit that has not returned, even one that ignores its token.</param>
    /// <returns>How the commit ended.</returns>
    Task<RacedResult<bool>> CommitAsync(IReadOnlyList<TItem> batch, CancellationToken abort);
}
