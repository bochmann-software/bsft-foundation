using Bsft.Messaging.Consuming;

namespace Bsft.Messaging.Loopback;

/// <summary>
/// One delivery from <see cref="LoopbackMessageTransport"/>. Acknowledging costs nothing because the
/// message was already taken out of the queue to make the delivery; retrying puts it back, at once or
/// on a timer when the consumer asked for a wait; parking hands it to the transport's parked list.
/// </summary>
internal sealed class LoopbackDelivery : IMessageDelivery
{
    private readonly ILoopbackMessageSink _sink;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackDelivery"/> class.
    /// </summary>
    /// <param name="sink">The transport this delivery came from.</param>
    /// <param name="message">The message being delivered.</param>
    /// <param name="deliveryAttempt">The one-based number of this delivery attempt.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public LoopbackDelivery(ILoopbackMessageSink sink, IntegrationEventMessage message, int deliveryAttempt)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        DeliveryAttempt = deliveryAttempt;
    }

    /// <inheritdoc />
    public IntegrationEventMessage Message { get; }

    /// <inheritdoc />
    public int DeliveryAttempt { get; }

    /// <inheritdoc />
    public Task AcknowledgeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RetryAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
        _sink.RedeliverAsync(Message, DeliveryAttempt + 1, delay, cancellationToken);

    /// <inheritdoc />
    public Task ParkAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (reason is null)
            throw new ArgumentNullException(nameof(reason));

        _sink.Park(Message, reason);

        return Task.CompletedTask;
    }
}
