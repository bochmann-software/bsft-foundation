using System.Threading.Channels;

namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// The consumer side of a bounded buffer: what the batching worker drains and what completes the buffer
/// when the host stops.
/// </summary>
/// <typeparam name="TItem">The queued item.</typeparam>
internal interface IBoundedBufferSource<TItem>
{
    /// <summary>Gets the reader of the queued items. There is exactly one reader.</summary>
    ChannelReader<TItem> Reader { get; }

    /// <summary>
    /// Stops the buffer accepting items. What is already queued stays readable, so a consumer drains it
    /// instead of losing it.
    /// </summary>
    void Complete();
}
