namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// Which kind of queue the adapter declares for this application.
/// </summary>
/// <remarks>
/// It is a choice about redelivery counting, not only about replication. The consumer host bounds a
/// message's attempts against <c>IMessageDelivery.DeliveryAttempt</c>, and AMQP only carries that count
/// on a quorum queue's <c>x-delivery-count</c> header. On a classic queue every delivery claims to be
/// the first, so a message that always fails is retried forever.
/// </remarks>
public enum RabbitMQQueueType
{
    /// <summary>
    /// A quorum queue. It is the default, because it is the only queue type that tells a consumer how
    /// often a message has been delivered, and so the only one on which the host's attempt budget means
    /// anything.
    /// </summary>
    Quorum,

    /// <summary>
    /// A classic queue. Choose it only where a quorum queue is not an option — a single-node broker
    /// without the feature, or an existing queue that was declared classic and cannot be converted —
    /// and bound redelivery some other way, because the host cannot.
    /// </summary>
    Classic,
}
