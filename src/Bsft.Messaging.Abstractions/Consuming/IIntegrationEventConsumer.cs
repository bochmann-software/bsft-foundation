namespace Bsft.Messaging.Consuming;

/// <summary>
/// Consumes one delivery end to end: decode, handle, and tell the transport what became of it.
/// </summary>
public interface IIntegrationEventConsumer
{
    /// <summary>
    /// Consumes a single delivery and completes it — acknowledged when it was handled or recognised as
    /// a redelivery, handed back when the failure may not repeat, parked when it will.
    /// </summary>
    /// <param name="delivery">The delivery to consume.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes once the delivery has been completed toward the transport.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="delivery"/> is null.</exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled while the delivery is in flight. The delivery is
    /// then left uncompleted on purpose, so the transport redelivers it to whoever is still running.
    /// </exception>
    /// <remarks>
    /// Short of that cancellation it does not throw for anything the message or its handlers did:
    /// every such outcome ends in the delivery being completed one way or another. What it does not
    /// absorb is a transport that fails to accept the completion, because a consumer that cannot tell
    /// the transport anything has nothing useful left to do with the next message either.
    /// </remarks>
    Task ConsumeAsync(IMessageDelivery delivery, CancellationToken cancellationToken = default);
}
