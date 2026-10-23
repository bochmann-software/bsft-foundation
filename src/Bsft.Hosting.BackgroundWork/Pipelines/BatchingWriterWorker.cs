using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// The hosted worker that drains an <see cref="IBoundedBuffer{TItem}"/> in batches and commits each one
/// through an <see cref="IBatchCommitter{TItem}"/>.
/// </summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
/// <remarks>
/// <para>
/// A batch is closed by size (<see cref="BsftBoundedPipelineOptions.MaxBatchSize"/>) or by time
/// (<see cref="BsftBoundedPipelineOptions.FlushInterval"/> after its first item), whichever comes first.
/// A batch that fails is logged and counted and the next one proceeds: one bad batch never takes the
/// worker, and with it the host, down.
/// </para>
/// <para>
/// On stop the buffer stops accepting items and the worker commits what is still queued, within
/// <see cref="BsftBoundedPipelineOptions.FlushOnStopTimeout"/>, whatever the state of the host's stop
/// token. A commit that does not return by then is abandoned through <see cref="IOperationRacer"/>
/// and what is left is counted as failed, so <see cref="StopAsync"/> never hangs on a committer that
/// ignores its token.
/// </para>
/// </remarks>
internal sealed class BatchingWriterWorker<TItem> : IHostedService, IDisposable
{
    private readonly IBoundedBufferSource<TItem> _source;
    private readonly IPipelineUpstream<TItem>? _upstream;
    private readonly IBatchCommitRunner<TItem> _committer;
    private readonly IPipelineCounterSink<TItem> _counters;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatchingWriterWorker<TItem>> _logger;
    private readonly BsftBoundedPipelineOptions _options;
    private readonly CancellationTokenSource _stopDeadline;
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationToken _stopDeadlineToken;
    private readonly CancellationToken _stoppingToken;
    private Task? _loop;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="BatchingWriterWorker{TItem}"/> class.</summary>
    /// <param name="source">The buffer producers write to.</param>
    /// <param name="committer">Commits the batches and abandons a commit that outlives the stop deadline.</param>
    /// <param name="counters">Counts what is committed and lost.</param>
    /// <param name="options">Supplies the pipeline's named options.</param>
    /// <param name="timeProvider">Paces the batches and the stop deadline.</param>
    /// <param name="logger">Receives failed batches.</param>
    /// <param name="upstream">The enrichment stage in front of the buffer, when there is one.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public BatchingWriterWorker(
        IBoundedBufferSource<TItem> source,
        IBatchCommitRunner<TItem> committer,
        IPipelineCounterSink<TItem> counters,
        IOptionsMonitor<BsftBoundedPipelineOptions> options,
        TimeProvider timeProvider,
        ILogger<BatchingWriterWorker<TItem>> logger,
        IPipelineUpstream<TItem>? upstream = null)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _upstream = upstream;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Get(BsftBoundedPipelineOptions.NameFor<TItem>());
        _stopDeadline = new CancellationTokenSource(Timeout.InfiniteTimeSpan, _timeProvider);
        _stopDeadlineToken = _stopDeadline.Token;
        _stoppingToken = _stopping.Token;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // A task of its own that is never started under a cancelled token: the stop that follows a start
        // at once still reaches a loop that runs, and flushes what was queued in between.
        _loop = Task.Run(RunAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The host's stop token is deliberately not consulted. The final flush has the budget of
    /// <see cref="BsftBoundedPipelineOptions.FlushOnStopTimeout"/> whatever state the token is in: a stage
    /// in front of this worker may already have used up the host's shutdown time, and a flush that
    /// inherited that would lose everything still buffered. The wait for the loop is bounded by that
    /// budget, not by the token.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // A stop after the disposal has nothing left to arm: the loop is already on its way out.
        if (Volatile.Read(ref _disposed) == 1)
        {
            if (_loop is not null)
                await _loop.ConfigureAwait(false);

            return;
        }

        await WaitForUpstreamAsync().ConfigureAwait(false);

        // Completing the buffer lets the loop drain what is left instead of dropping it, and the
        // deadline bounds how long that drain may take.
        _source.Complete();
        _stopDeadline.CancelAfter(_options.FlushOnStopTimeout);
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
            await _loop.ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the enrichment stage in front of the buffer to hand on its last items, so a host that
    /// stops every service at once loses nothing to a buffer that is already complete.
    /// </summary>
    /// <returns>A task that completes when the stage has finished or the wait ran out.</returns>
    private async Task WaitForUpstreamAsync()
    {
        if (_upstream is null)
            return;

        try
        {
            await _upstream.Completion
                .WaitAsync(_options.EnrichmentOnStopTimeout + _options.FlushOnStopTimeout, _timeProvider, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("The enrichment stage of {Item} did not finish in time; the writer stops without it.", typeof(TItem).Name);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Disposing tells a loop that is still running to give up, and releases the cancellation sources only
    /// once it has: a host that stopped waiting must not leave a loop reading disposed sources.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        // The deadline first, the completion last: completing the buffer wakes a loop that holds a batch,
        // and that loop must find the deadline passed, so it never starts a commit while the container
        // around it is going away.
        _stopDeadline.Cancel();
        _stopping.Cancel();
        _source.Complete();
        if (_loop is null || _loop.IsCompleted)
        {
            ReleaseSources();
            return;
        }

        _ = _loop.ContinueWith(
            static (loop, worker) =>
            {
                _ = loop.Exception;
                ((BatchingWriterWorker<TItem>)worker!).ReleaseSources();
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ReleaseSources()
    {
        _stopDeadline.Dispose();
        _stopping.Dispose();
    }

    private async Task RunAsync()
    {
        var stoppingToken = _stoppingToken;
        try
        {
            while (await _source.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                var batch = await CollectAsync(stoppingToken).ConfigureAwait(false);
                await CommitAsync(batch).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("The batching writer of {Item} is stopping.", typeof(TItem).Name);
        }

        await FlushAsync().ConfigureAwait(false);
    }

    /// <summary>Gathers a batch until it is full, its interval has passed or the host stops.</summary>
    /// <param name="stoppingToken">The token that is cancelled when the host stops.</param>
    /// <returns>The batch, never empty unless another reader took its items.</returns>
    private async Task<List<TItem>> CollectAsync(CancellationToken stoppingToken)
    {
        var batch = new List<TItem>(Math.Min(_options.MaxBatchSize, _options.Capacity));
        using var interval = new CancellationTokenSource(_options.FlushInterval, _timeProvider);
        using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, interval.Token);

        while (batch.Count < _options.MaxBatchSize)
        {
            if (_source.Reader.TryRead(out var item))
                batch.Add(item);
            else if (!await HasMoreAsync(window.Token).ConfigureAwait(false))
                break;
        }

        return batch;
    }

    /// <summary>Waits for the next item, and says whether one came before the window closed.</summary>
    /// <param name="window">Closes when the interval passes or the host stops.</param>
    /// <returns><c>true</c> when an item can be read; <c>false</c> when the window closed or the buffer is complete and empty.</returns>
    private async Task<bool> HasMoreAsync(CancellationToken window)
    {
        try
        {
            return await _source.Reader.WaitToReadAsync(window).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Commits what is queued at the stop, in batches, until it is empty or the deadline passes.</summary>
    /// <returns>A task that completes when the queue is empty or abandoned.</returns>
    private async Task FlushAsync()
    {
        var batch = new List<TItem>(Math.Min(_options.MaxBatchSize, _options.Capacity));
        while (!_stopDeadlineToken.IsCancellationRequested && _source.Reader.TryRead(out var item))
        {
            batch.Add(item);
            if (batch.Count == _options.MaxBatchSize)
            {
                await CommitAsync(batch).ConfigureAwait(false);
                batch = new List<TItem>(batch.Capacity);
            }
        }

        await CommitAsync(batch).ConfigureAwait(false);

        var abandoned = 0;
        while (_source.Reader.TryRead(out _))
            abandoned++;

        if (abandoned > 0)
        {
            _logger.LogWarning(
                "The stop deadline passed with {Count} {Item} items still queued; they are lost.", abandoned, typeof(TItem).Name);
            _counters.CountAbandoned(abandoned);
        }
    }

    /// <summary>Commits one batch and isolates its failure.</summary>
    /// <param name="batch">The batch; an empty one is skipped.</param>
    /// <returns>A task that completes when the batch is committed, failed or abandoned.</returns>
    private async Task CommitAsync(List<TItem> batch)
    {
        if (batch.Count == 0)
            return;

        var outcome = await _committer.CommitAsync(batch, _stopDeadlineToken).ConfigureAwait(false);

        if (outcome.Status == RacedStatus.Completed)
        {
            _counters.CountCommitted(batch.Count);
            return;
        }

        _logger.LogWarning(
            outcome.Fault,
            "A batch of {Count} {Item} items could not be committed and is lost{Reason}.",
            batch.Count,
            typeof(TItem).Name,
            outcome.Status == RacedStatus.GaveUp ? " at the stop deadline" : string.Empty);
        _counters.CountFailedBatch(batch.Count);
    }
}
