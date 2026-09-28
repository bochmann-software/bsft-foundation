using RabbitMQ.Client;
using System.Text;

namespace Bsft.Messaging.RabbitMQ.Tests.Support;

/// <summary>
/// Builds the deliveries the consuming tests push through a fake channel, so that a test states only
/// the one thing it varies.
/// </summary>
internal static class TestDeliveries
{
    /// <summary>The event name every delivery here carries, matching the topology the tests declare.</summary>
    public const string EventName = "orders.placed.v1";

    /// <summary>Builds a well-formed delivery.</summary>
    /// <param name="deliveryTag">The tag the settlement has to quote.</param>
    /// <param name="messageId">The message id, defaulting to a fresh event id.</param>
    /// <param name="headers">The AMQP headers, defaulting to none.</param>
    /// <param name="body">The body, defaulting to an empty JSON object.</param>
    /// <param name="bodyBuffer">
    /// The exact buffer to deliver the body in, for a test that then reuses it the way the client
    /// reuses its pooled one. Overrides <paramref name="body"/> when given.
    /// </param>
    /// <returns>The delivery.</returns>
    public static RabbitMQDelivery Delivery(
        ulong deliveryTag = 1,
        string? messageId = null,
        IDictionary<string, object?>? headers = null,
        string body = "{}",
        byte[]? bodyBuffer = null) => new()
    {
        DeliveryTag = deliveryTag,
        RoutingKey = EventName,
        Properties = new BasicProperties
        {
            MessageId = messageId ?? Guid.NewGuid().ToString(),
            Type = EventName,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = headers,
        },
        Body = bodyBuffer ?? Encoding.UTF8.GetBytes(body),
    };

    /// <summary>Builds a delivery a quorum queue has already redelivered the given number of times.</summary>
    /// <param name="deliveryTag">The tag the settlement has to quote.</param>
    /// <param name="deliveryCount">What the broker reports in <c>x-delivery-count</c>.</param>
    /// <returns>The delivery.</returns>
    public static RabbitMQDelivery Redelivery(ulong deliveryTag, long deliveryCount) =>
        Delivery(
            deliveryTag,
            headers: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.DeliveryCountHeader] = deliveryCount,
            });
}
