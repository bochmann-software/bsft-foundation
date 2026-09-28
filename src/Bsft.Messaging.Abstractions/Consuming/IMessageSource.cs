namespace Bsft.Messaging.Consuming;

/// <summary>
/// The receiving half of the transport seam: where the consumer host gets its messages from.
/// <see cref="IMessageBus"/> is the sending half, and a broker adapter implements both in its own
/// package — an in-process loopback implements both for a single-service deployment and for tests.
/// </summary>
/// <remarks>
/// <para>
/// The seam is deliberately narrow. A source produces <see cref="IMessageDelivery"/> instances and is
/// told the outcome of each through that delivery; it is never called back into the host, and it needs
/// nothing from the framework beyond these two types. That is what lets a broker adapter be written
/// against this contract alone, without sharing code with the host or knowing that an inbox, a type
/// registry or a handler exists.
/// </para>
/// <para>
/// Everything the transport is good at stays on the transport's side of the seam: how many messages it
/// prefetches, how it spreads them across instances, how long it waits before reconnecting. The host
/// contributes the things a transport cannot know — which event a name means, whether this consumer has
/// already processed the message, and what the handlers made of it.
/// </para>
/// </remarks>
public interface IMessageSource
{
    /// <summary>
    /// Streams messages as the transport delivers them, until the token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token to observe. Cancelling it ends the stream; a source ends it by returning
    /// rather than by throwing, which is why the host reads a cancelled enumeration as its own shutdown.
    /// </param>
    /// <returns>
    /// The deliveries, in the order the transport offers them. The stream is lazy: a source waits for
    /// the next message inside the enumeration rather than buffering ahead of the host, so the host
    /// holding one message is the backpressure the transport sees.
    /// </returns>
    /// <remarks>
    /// The host consumes the stream one delivery at a time and completes each before asking for the
    /// next. A stream that ends on its own — a broker connection that dropped, a channel that closed —
    /// is not an error: the host waits out its restart delay and enumerates again, so a source may
    /// treat the end of a stream as its reconnection point. A source that instead throws has its
    /// failure logged in full and is restarted the same way, so neither shape of failure takes the
    /// host down.
    /// </remarks>
    IAsyncEnumerable<IMessageDelivery> ReceiveAsync(CancellationToken cancellationToken = default);
}
