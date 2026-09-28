using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Diagnostics.CodeAnalysis;

namespace Bsft.Messaging.RabbitMQ.Connections;

/// <summary>
/// Forwards <see cref="IRabbitMQChannel"/> onto the client's <see cref="IChannel"/>. It holds no logic
/// of its own beyond translating one call shape into another, which is why the whole type is excluded
/// from coverage: every line of it needs a live broker to run, and a test that could reach it would be
/// asserting the client's behaviour rather than ours.
/// </summary>
[ExcludeFromCodeCoverage] // pure delegation to RabbitMQ.Client; unreachable without a live broker
internal sealed class RabbitMQChannelAdapter : IRabbitMQChannel
{
    private readonly IChannel _channel;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQChannelAdapter"/> class.
    /// </summary>
    /// <param name="channel">The channel to forward to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="channel"/> is null.</exception>
    public RabbitMQChannelAdapter(IChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <inheritdoc />
    public bool IsOpen => _channel.IsOpen;

    /// <inheritdoc />
    public Task DeclareExchangeAsync(string exchange, string type, CancellationToken cancellationToken = default) =>
        _channel.ExchangeDeclareAsync(exchange, type, durable: true, autoDelete: false, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task DeclareQueueAsync(
        string queue, IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default) =>
        _channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task BindQueueAsync(
        string queue, string exchange, string routingKey, CancellationToken cancellationToken = default) =>
        _channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task SetPrefetchAsync(ushort prefetchCount, CancellationToken cancellationToken = default) =>
        _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetchCount, global: false, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        // mandatory is false on purpose: an integration event with no queue bound to its name is normal
        // in publish/subscribe — the consumers may live in services that are not deployed yet, or not
        // interested — and asking the broker to return such a message would turn that into a publish
        // failure the outbox retries forever.
        await _channel
            .BasicPublishAsync(
                exchange,
                routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ConsumeAsync(
        string queue, Func<RabbitMQDelivery, Task> onDeliveryAsync, CancellationToken cancellationToken = default)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);

        // delivery.Body is memory the client rented from a pool and returns as soon as this callback
        // completes, while the RabbitMQDelivery built from it lives for the whole delivery and is read
        // again at the far end of it, when a park republishes the body. RabbitMQDelivery.Body copies on
        // assignment for exactly that reason, so what is handed on here is owned rather than borrowed.
        consumer.ReceivedAsync += (_, delivery) => onDeliveryAsync(new RabbitMQDelivery
        {
            DeliveryTag = delivery.DeliveryTag,
            RoutingKey = delivery.RoutingKey,
            IsRedelivered = delivery.Redelivered,
            Properties = delivery.BasicProperties,
            Body = delivery.Body,
        });

        return await _channel
            .BasicConsumeAsync(
                queue,
                autoAck: false,
                consumerTag: string.Empty,
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task AckAsync(ulong deliveryTag, CancellationToken cancellationToken = default) =>
        _channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: cancellationToken).AsTask();

    /// <inheritdoc />
    public Task NackAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default) =>
        _channel.BasicNackAsync(deliveryTag, multiple: false, requeue: requeue, cancellationToken: cancellationToken).AsTask();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _channel.DisposeAsync();
}
