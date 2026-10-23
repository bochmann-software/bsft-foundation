using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// The bounded enrichment stage in front of the batching worker: one worker takes each item through an
/// <see cref="IPipelineEnricher{TItem}"/> and hands the result on to the <see cref="IBoundedBuffer{TItem}"/>.
/// </summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
/// <remarks>
/// <para>
/// Every enrichment is raced against <see cref="BsftBoundedPipelineOptions.EnrichmentTimeout"/> and an
/// abort token through <see cref="IOperationRacer"/>, so an enricher that ignores its token can neither
/// hang the worker nor the host's stop. A fault, a <c>null</c> result, a timeout and an abort are each
/// one counted failure and the raw item goes on in place of the enriched one.
/// </para>
/// <para>
/// On stop the stage stops accepting items and enriches what is queued for at most
/// <see cref="BsftBoundedPipelineOptions.EnrichmentOnStopTimeout"/>, or until the host's stop token is
/// cancelled if that comes first; from then on the remaining items pass through raw, counted once and
/// logged once. That budget is the stage's own, so it cannot use up the shutdown time the batching writer
/// needs for its flush. The stage is registered after the batching worker, which makes the host stop it
/// first, so its output still finds an open buffer. Items do not keep their order: one that finds the
/// stage full goes on raw ahead of older items still waiting for their enrichment.
/// </para>
/// </remarks>
internal sealed class EnrichmentStageWorker<TItem> : IEnrichmentStage<TItem>, IPipelineUpstream<TItem>, IHostedService, IDisposable
    where TItem : class
{
    private readonly Channel<TItem> _channel;
    private readonly IBoundedBuffer<TItem> _output;
    private readonly IPipelineEnricher<TItem> _enricher;
    private readonly IPipelineCounterSink<TItem> _counters;
    private readonly IOperationRacer _racer;
    private readonly ILogger<EnrichmentStageWorker<TItem>> _logger;
    private readonly TimeSpan _enrichmentTimeout;
    private readonly CancellationTokenSource _abort;
    private readonly CancellationToken _abortToken;
    private readonly TimeSpan _onStopTimeout;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="EnrichmentStageWorker{TItem}"/> class.</summary>
    /// <param name="output">Receives the finished items.</param>
    /// <param name="enricher">Enriches one item.</param>
    /// <param name="counters">Counts skipped and failed enrichments.</param>
    /// <param name="racer">Abandons an enrichment that outlives its limit.</param>
    /// <param name="options">Supplies the pipeline's named options.</param>
    /// <param name="timeProvider">Measures the stop budget.</param>
    /// <param name="logger">Receives failed enrichments.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public EnrichmentStageWorker(
        IBoundedBuffer<TItem> output,
        IPipelineEnricher<TItem> enricher,
        IPipelineCounterSink<TItem> counters,
        IOperationRacer racer,
        IOptionsMonitor<BsftBoundedPipelineOptions> options,
        TimeProvider timeProvider,
        ILogger<EnrichmentStageWorker<TItem>> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _output = output ?? throw new ArgumentNullException(nameof(output));
        _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _racer = racer ?? throw new ArgumentNullException(nameof(racer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (timeProvider is null)
            throw new ArgumentNullException(nameof(timeProvider));

        _abort = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
        _abortToken = _abort.Token;
        var settings = options.Get(BsftBoundedPipelineOptions.NameFor<TItem>());
        _enrichmentTimeout = settings.EnrichmentTimeout;
        _onStopTimeout = settings.EnrichmentOnStopTimeout;
        _channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(settings.EnrichmentCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public Task Completion => _completion.Task;

    /// <inheritdoc />
    public bool TryEnqueue(TItem item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        if (_channel.Writer.TryWrite(item))
            return true;

        _counters.CountEnrichmentSkipped(1);
        _output.TryEnqueue(item);
        return false;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _worker = Task.Run(RunAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        if (_worker is null)
        {
            _completion.TrySetResult();
            return;
        }

        // A stop after the disposal has nothing left to arm: the loop is already on its way out.
        if (Volatile.Read(ref _disposed) == 1)
        {
            await _worker.ConfigureAwait(false);
            return;
        }

        // The stage's own budget, and the host's token on top of it: whichever ends first passes the
        // rest of the queue on raw. The registration outlives the wait for the worker.
        _abort.CancelAfter(_onStopTimeout);
        await using var registration = cancellationToken.Register(_abort.Cancel).ConfigureAwait(false);
        await _worker.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        // Completing the channel lets a worker that was never stopped drain what is queued, raw, and end.
        _channel.Writer.TryComplete();
        _abort.Cancel();
        if (_worker is null)
        {
            _completion.TrySetResult();
            _abort.Dispose();
            return;
        }

        if (_worker.IsCompleted)
        {
            _abort.Dispose();
            return;
        }

        _ = _worker.ContinueWith(
            static (worker, abort) =>
            {
                _ = worker.Exception;
                ((CancellationTokenSource)abort!).Dispose();
            },
            _abort,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunAsync()
    {
        try
        {
            await DrainAsync().ConfigureAwait(false);
        }
        finally
        {
            _completion.TrySetResult();
        }
    }

    private async Task DrainAsync()
    {
        var passedOnRaw = 0;
        await foreach (var item in _channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            // Past the stop budget the rest goes on without enrichment, in bulk and silently per item.
            if (_abortToken.IsCancellationRequested)
            {
                passedOnRaw++;
                _output.TryEnqueue(item);
                continue;
            }

            _output.TryEnqueue(await EnrichAsync(item).ConfigureAwait(false));
        }

        if (passedOnRaw > 0)
        {
            _counters.CountEnrichmentSkipped(passedOnRaw);
            _logger.LogWarning(
                "The stop budget of the {Item} enrichment stage passed; {Count} items go on without enrichment.",
                typeof(TItem).Name,
                passedOnRaw);
        }
    }

    private async Task<TItem> EnrichAsync(TItem item)
    {
        var outcome = await _racer.RunAsync(
            token => _enricher.EnrichAsync(item, token).AsTask(),
            _enrichmentTimeout,
            _abortToken).ConfigureAwait(false);

        if (outcome is { Status: RacedStatus.Completed, Value: { } enriched })
            return enriched;

        _counters.CountEnrichmentFailed();
        _logger.LogWarning(
            outcome.Fault,
            "Enriching a {Item} {Reason}; it goes on without.",
            typeof(TItem).Name,
            Describe(outcome.Status));
        return item;
    }

    private static string Describe(RacedStatus status) => status switch
    {
        RacedStatus.Completed => "returned no item",
        RacedStatus.Faulted => "failed",
        _ => "was given up on",
    };
}
