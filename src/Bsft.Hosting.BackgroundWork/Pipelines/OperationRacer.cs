namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>Default <see cref="IOperationRacer"/>; its limit runs on the injected <see cref="TimeProvider"/>.</summary>
internal sealed class OperationRacer : IOperationRacer
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="OperationRacer"/> class.</summary>
    /// <param name="timeProvider">Measures the limit.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public OperationRacer(TimeProvider timeProvider) =>
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<RacedResult<TValue>> RunAsync<TValue>(
        Func<CancellationToken, Task<TValue>> operation,
        TimeSpan limit,
        CancellationToken abort)
    {
        if (operation is null)
            throw new ArgumentNullException(nameof(operation));

        if (abort.IsCancellationRequested)
            return new RacedResult<TValue>(RacedStatus.GaveUp, default, null);

        using var limitSource = new CancellationTokenSource(limit, _timeProvider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(abort, limitSource.Token);

        // A task of its own, so a throw before the first await ends up in the task rather than here.
        var running = Task.Run(() => RunAsync(operation, deadline.Token), CancellationToken.None);
        var giveUp = Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
        var finished = await Task.WhenAny(running, giveUp).ConfigureAwait(false);

        // Read before the cancel below, which would make every run look like one that hit its limit.
        var expired = deadline.IsCancellationRequested;

        // Releases the pending delay on both paths.
        await deadline.CancelAsync().ConfigureAwait(false);

        if (finished != running)
        {
            // Nobody awaits the abandoned task any more: its late fault is observed here.
            _ = running.ContinueWith(
                static abandoned => _ = abandoned.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return new RacedResult<TValue>(RacedStatus.GaveUp, default, null);
        }

        if (running.IsFaulted)
            return new RacedResult<TValue>(RacedStatus.Faulted, default, running.Exception?.GetBaseException());

        // An operation that honored its token at the limit or the abort finished off the same cancel the
        // give-up signal came from; which of the two WhenAny returned is chance, so both are read as
        // having been given up on.
        var result = await running.ConfigureAwait(false);
        return expired && result.Fault is OperationCanceledException
            ? new RacedResult<TValue>(RacedStatus.GaveUp, default, null)
            : result;
    }

    /// <summary>
    /// Runs the operation and keeps a cancellation it throws as the fault it is: a cancelled task has no
    /// exception to read, and the message and stack of the real cancellation (an HTTP client's timeout,
    /// say) belong in the log.
    /// </summary>
    /// <typeparam name="TValue">The value the operation produces.</typeparam>
    /// <param name="operation">The operation.</param>
    /// <param name="cancellationToken">The token the operation is given.</param>
    /// <returns>The completed result, or the cancellation as a faulted one; any other exception faults the task.</returns>
    private static async Task<RacedResult<TValue>> RunAsync<TValue>(Func<CancellationToken, Task<TValue>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return new RacedResult<TValue>(RacedStatus.Completed, await operation(cancellationToken).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException canceled)
        {
            return new RacedResult<TValue>(RacedStatus.Faulted, default, canceled);
        }
    }
}
