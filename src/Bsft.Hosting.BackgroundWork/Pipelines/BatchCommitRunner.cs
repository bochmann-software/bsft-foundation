namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>Default <see cref="IBatchCommitRunner{TItem}"/>: the committer, raced against the abort token.</summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
internal sealed class BatchCommitRunner<TItem> : IBatchCommitRunner<TItem>
{
    private readonly IBatchCommitter<TItem> _committer;
    private readonly IOperationRacer _racer;

    /// <summary>Initializes a new instance of the <see cref="BatchCommitRunner{TItem}"/> class.</summary>
    /// <param name="committer">Commits the batches.</param>
    /// <param name="racer">Abandons a commit that outlives the abort.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public BatchCommitRunner(IBatchCommitter<TItem> committer, IOperationRacer racer)
    {
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        _racer = racer ?? throw new ArgumentNullException(nameof(racer));
    }

    /// <inheritdoc />
    public Task<RacedResult<bool>> CommitAsync(IReadOnlyList<TItem> batch, CancellationToken abort) =>
        _racer.RunAsync(
            async token =>
            {
                await _committer.CommitAsync(batch, token).ConfigureAwait(false);
                return true;
            },
            Timeout.InfiniteTimeSpan,
            abort);
}
