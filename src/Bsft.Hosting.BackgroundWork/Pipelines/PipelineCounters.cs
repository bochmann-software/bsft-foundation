using System.Diagnostics.Metrics;
using Bsft.Instrumentation;

namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// Default <see cref="IPipelineCounterSink{TItem}"/>: lock-free totals that also feed the
/// <c>bsft.pipeline.items</c> counter of the <see cref="BackgroundWorkDefaults.PipelineInstrumentationSourceName"/> instrumentation source.
/// </summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
internal sealed class PipelineCounters<TItem> : IPipelineCounterSink<TItem>
{
    /// <summary>The name of the counter every outcome is added to.</summary>
    internal const string CounterName = "bsft.pipeline.items";

    private readonly Counter<long> _items;
    private readonly string _pipeline = BsftBoundedPipelineOptions.NameFor<TItem>();
    private long _enqueued;
    private long _dropped;
    private long _committed;
    private long _failed;
    private long _failedBatches;
    private long _enrichmentFailed;
    private long _enrichmentSkipped;

    /// <summary>Initializes a new instance of the <see cref="PipelineCounters{TItem}"/> class.</summary>
    /// <param name="sources">Supplies the instrumentation source the counter is created on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources"/> is null.</exception>
    public PipelineCounters(IInstrumentationSourceProvider sources)
    {
        if (sources is null)
            throw new ArgumentNullException(nameof(sources));

        _items = sources.Get(BackgroundWorkDefaults.PipelineInstrumentationSourceName).GetOrCreateCounter(CounterName);
    }

    /// <inheritdoc />
    public long Enqueued => Interlocked.Read(ref _enqueued);

    /// <inheritdoc />
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    public long Committed => Interlocked.Read(ref _committed);

    /// <inheritdoc />
    public long Failed => Interlocked.Read(ref _failed);

    /// <inheritdoc />
    public long FailedBatches => Interlocked.Read(ref _failedBatches);

    /// <inheritdoc />
    public long EnrichmentFailed => Interlocked.Read(ref _enrichmentFailed);

    /// <inheritdoc />
    public long EnrichmentSkipped => Interlocked.Read(ref _enrichmentSkipped);

    /// <inheritdoc />
    public void CountEnqueued()
    {
        Interlocked.Increment(ref _enqueued);
        Emit("enqueued", 1);
    }

    /// <inheritdoc />
    public void CountDropped()
    {
        Interlocked.Increment(ref _dropped);
        Emit("dropped", 1);
    }

    /// <inheritdoc />
    public void CountCommitted(int count)
    {
        Interlocked.Add(ref _committed, count);
        Emit("committed", count);
    }

    /// <inheritdoc />
    public void CountFailedBatch(int count)
    {
        Interlocked.Add(ref _failed, count);
        Interlocked.Increment(ref _failedBatches);
        Emit("failed", count);
    }

    /// <inheritdoc />
    public void CountAbandoned(int count)
    {
        Interlocked.Add(ref _failed, count);
        Emit("failed", count);
    }

    /// <inheritdoc />
    public void CountEnrichmentFailed()
    {
        Interlocked.Increment(ref _enrichmentFailed);
        Emit("enrichment_failed", 1);
    }

    /// <inheritdoc />
    public void CountEnrichmentSkipped(int count)
    {
        Interlocked.Add(ref _enrichmentSkipped, count);
        Emit("enrichment_skipped", count);
    }

    private void Emit(string outcome, long count) =>
        _items.Add(
            count,
            new KeyValuePair<string, object?>("pipeline", _pipeline),
            new KeyValuePair<string, object?>("outcome", outcome));
}
