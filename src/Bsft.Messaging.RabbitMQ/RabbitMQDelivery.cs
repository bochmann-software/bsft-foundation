using RabbitMQ.Client;

namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// One message as it arrived from the broker, before anything has been made of it: the AMQP metadata,
/// the raw body, and the delivery tag the acknowledgement has to quote.
/// </summary>
/// <remarks>
/// <para>
/// It carries the client's own <see cref="IReadOnlyBasicProperties"/> rather than a copy of the fields
/// this adapter happens to read. Copying would decide, here, which properties a consumer may ever look
/// at, and it would hide a mapping mistake behind a second mapping.
/// </para>
/// <para>
/// A class rather than a record, although it is plain data: the body is a
/// <see cref="ReadOnlyMemory{T}"/>, whose equality compares the span it points at rather than the
/// bytes, so the value semantics a record advertises would not hold for the field that matters most.
/// </para>
/// <para>
/// It <b>owns</b> its body. The client hands the body of a delivery out as memory rented from an
/// <c>ArrayPool</c> and takes it back the moment the receive callback returns, so anything read from
/// it afterwards is whatever the pool has since put there. This type outlives that callback by the
/// whole of a delivery — the park path republishes the body after the handlers have failed — so
/// <see cref="Body"/> copies on assignment rather than trusting each construction site to remember.
/// One array copy per message is the cost, next to a deserialization of the same bytes.
/// </para>
/// </remarks>
internal sealed class RabbitMQDelivery
{
    private readonly ReadOnlyMemory<byte> _body;

    /// <summary>
    /// Gets the broker's identifier for this delivery on its channel. It is what
    /// <see cref="Bsft.Messaging.RabbitMQ.IRabbitMQChannel.AckAsync"/> and <see cref="Bsft.Messaging.RabbitMQ.IRabbitMQChannel.NackAsync"/> quote, and
    /// it is only meaningful on the channel that received the delivery.
    /// </summary>
    public ulong DeliveryTag { get; init; }

    /// <summary>
    /// Gets the routing key the message was published under, which by convention is the event name.
    /// </summary>
    public string RoutingKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the broker has delivered this message before. It is a hint, not a count: the broker
    /// sets it on any redelivery without saying how many there were.
    /// </summary>
    public bool IsRedelivered { get; init; }

    /// <summary>Gets the AMQP properties of the message.</summary>
    public IReadOnlyBasicProperties Properties { get; init; } = new BasicProperties();

    /// <summary>
    /// Gets the message body as it came off the wire, as a copy this delivery owns. Assigning
    /// transport-rented memory is therefore safe, and reading it after the receive callback has
    /// returned is too — which is what the park path does.
    /// </summary>
    public ReadOnlyMemory<byte> Body
    {
        get => _body;
        init => _body = value.ToArray();
    }
}
