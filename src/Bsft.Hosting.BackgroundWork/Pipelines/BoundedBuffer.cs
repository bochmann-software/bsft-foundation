using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// Default <see cref="IBoundedBuffer{TItem}"/>: a bounded channel of
/// <see cref="BsftBoundedPipelineOptions.Capacity"/> items with a single reader.
/// </summary>
/// <typeparam name="TItem">The queued item.</typeparam>
/// <remarks>
/// <see cref="ChannelWriter{T}.TryWrite"/> on a bounded channel in
/// <see cref="BoundedChannelFullMode.Wait"/> mode returns at once when the channel is full, which is
/// what keeps a producer from ever waiting on the consumer.
/// </remarks>
internal sealed class BoundedBuffer<TItem> : IBoundedBuffer<TItem>, IBoundedBufferSource<TItem>
{
    private readonly Channel<TItem> _channel;
    private readonly IPipelineCounterSink<TItem> _counters;

    /// <summary>Initializes a new instance of the <see cref="BoundedBuffer{TItem}"/> class.</summary>
    /// <param name="options">Supplies the pipeline's named options.</param>
    /// <param name="counters">Counts what is queued and dropped.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public BoundedBuffer(IOptionsMonitor<BsftBoundedPipelineOptions> options, IPipelineCounterSink<TItem> counters)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        var capacity = options.Get(BsftBoundedPipelineOptions.NameFor<TItem>()).Capacity;
        _channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public ChannelReader<TItem> Reader => _channel.Reader;

    /// <inheritdoc />
    public bool TryEnqueue(TItem item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        if (!_channel.Writer.TryWrite(item))
        {
            _counters.CountDropped();
            return false;
        }

        _counters.CountEnqueued();
        return true;
    }

    /// <inheritdoc />
    public void Complete() => _channel.Writer.TryComplete();
}
