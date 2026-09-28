using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client;
using System.Text;
using Xunit;
using Bsft.Messaging.Consuming;
using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.DependencyInjection;

namespace Bsft.Messaging.RabbitMQ.Tests;

/// <summary>
/// The two halves joined: a message arrives on a fake AMQP channel, travels through the source into the
/// consumer host, past the inbox, into a typed handler, and the host's decision comes back out as the
/// acknowledgement the broker is told about. No broker is involved — only the thin client surface this
/// adapter narrowed <c>IChannel</c> to.
/// </summary>
public class RabbitMQSeamBindingTests : IAsyncLifetime
{
    private readonly FakeRabbitMQConnectionFactory _connectionFactory = new();
    private readonly FakeRabbitMQConnection _connection = new();
    private readonly FakeRabbitMQChannel _consumeChannel = new();
    private readonly FakeRabbitMQChannel _publishChannel = new();
    private readonly SeamHandlerLog _log = new();

    private ServiceProvider _provider = null!;
    private IHostedService _consumerHost = null!;

    public async Task InitializeAsync()
    {
        // One connection object serves both sides; it hands out the consuming channel first, because
        // the subscription is established before anything is ever published.
        _connection.WithChannel(_consumeChannel).WithChannel(_publishChannel);
        _connectionFactory.WithConnection(_connection).WithConnection(_connection);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_log);

        // Registered before the adapter, so the adapter's TryAdd finds the fake in place and the whole
        // pipeline runs against it rather than against a socket.
        services.AddSingleton<IRabbitMQConnectionFactory>(_connectionFactory);

        services.AddBsftRabbitMQMessaging(o =>
        {
            o.ApplicationName = "orders";
            o.UserName = TestOptions.UserName;
            o.Password = TestOptions.Password;
            o.Subscribe(TestDeliveries.EventName);
        });

        services.AddBsftMessaging(o => o.AddEvent<SeamOrderPlacedEvent>(TestDeliveries.EventName));
        services.AddBsftConsumerHost(o =>
        {
            o.ConsumerName = "orders";
            o.MaxDeliveryAttempts = 3;
            o.ReceiveRestartDelay = TimeSpan.FromMilliseconds(20);
        });
        services.AddBsftIntegrationEventHandler<SeamOrderPlacedEvent, SeamOrderPlacedHandler>();

        _provider = services.BuildServiceProvider();
        _consumerHost = _provider.GetServices<IHostedService>().OfType<IntegrationEventConsumerHost>().Single();

        await _consumerHost.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _consumeChannel.DeliveryCallback is not null);
    }

    public async Task DisposeAsync()
    {
        await _consumerHost.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task A_message_reaches_the_typed_handler_and_is_acknowledged_on_its_channel()
    {
        await _consumeChannel.DeliverAsync(Delivery(1, "A-1"));

        await Eventually.HoldsAsync(() => _consumeChannel.Acked.Count == 1);
        _log.Handled.Should().Equal("A-1");
        _consumeChannel.Nacked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_redelivery_of_a_message_the_inbox_already_recorded_is_acknowledged_without_handling_it()
    {
        var eventId = Guid.NewGuid();
        await _consumeChannel.DeliverAsync(Delivery(1, "A-1", eventId));
        await Eventually.HoldsAsync(() => _consumeChannel.Acked.Count == 1);

        await _consumeChannel.DeliverAsync(Delivery(2, "A-1", eventId));

        await Eventually.HoldsAsync(() => _consumeChannel.Acked.Count == 2);
        _log.Handled.Should().Equal("A-1");
    }

    [Fact]
    public async Task A_handler_failure_with_attempts_left_is_handed_back_as_a_requeue()
    {
        _log.Fails = true;

        await _consumeChannel.DeliverAsync(Delivery(1, "A-2"));

        await Eventually.HoldsAsync(() => _consumeChannel.Nacked.Count == 1);
        _consumeChannel.Nacked.Should().Equal((1UL, true));
        _consumeChannel.Acked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_message_whose_attempts_are_spent_is_parked_to_the_dead_letter_exchange_with_the_reason()
    {
        // The broker says this is the third delivery, and the host allows three, so this one parks.
        _log.Fails = true;

        await _consumeChannel.DeliverAsync(Delivery(1, "A-3", deliveryCount: 2));

        await Eventually.HoldsAsync(() => _publishChannel.Published.Count == 1);
        var parked = _publishChannel.Published[0];
        parked.Exchange.Should().Be("orders.events.dlx");
        parked.RoutingKey.Should().Be(TestDeliveries.EventName);
        parked.Properties.Headers![RabbitMQMessageMapper.ParkReasonHeader]
            .Should().BeOfType<string>()
            .Which.Should().StartWith($"{MessageParkReason.AttemptsExhausted}:");

        // Acknowledged only after the copy was confirmed: the original leaves the queue once, and only
        // once something else holds the message.
        await Eventually.HoldsAsync(() => _consumeChannel.Acked.Count == 1);
        _consumeChannel.Acked.Should().Equal(1UL);
    }

    [Fact]
    public async Task A_park_the_broker_would_not_take_falls_back_to_a_rejection_so_the_message_still_lands_there()
    {
        _log.Fails = true;
        _publishChannel.PublishBehaviour = _ => throw new OperationInterruptedException();

        await _consumeChannel.DeliverAsync(Delivery(1, "A-4", deliveryCount: 2));

        await Eventually.HoldsAsync(() => _consumeChannel.Nacked.Count == 1);
        _consumeChannel.Nacked.Should().Equal((1UL, false));
        _consumeChannel.Acked.Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_this_consumer_was_never_told_about_is_parked_rather_than_guessed_at()
    {
        var delivery = Delivery(1, "A-5");
        delivery = new RabbitMQDelivery
        {
            DeliveryTag = delivery.DeliveryTag,
            RoutingKey = "inventory.reserved.v1",
            Properties = new BasicProperties
            {
                MessageId = delivery.Properties.MessageId,
                Type = "inventory.reserved.v1",
                Timestamp = delivery.Properties.Timestamp,
                Headers = delivery.Properties.Headers,
            },
            Body = delivery.Body,
        };

        await _consumeChannel.DeliverAsync(delivery);

        await Eventually.HoldsAsync(() => _publishChannel.Published.Count == 1);
        _publishChannel.Published[0].Properties.Headers![RabbitMQMessageMapper.ParkReasonHeader]
            .Should().BeOfType<string>()
            .Which.Should().StartWith($"{MessageParkReason.UnknownEventName}:");
    }

    [Fact]
    public async Task A_delivery_that_is_not_an_envelope_is_parked_by_the_transport_without_troubling_the_host()
    {
        await _consumeChannel.DeliverAsync(TestDeliveries.Delivery(1, messageId: "not-a-guid"));

        await Eventually.HoldsAsync(() => _publishChannel.Published.Count == 1);
        _publishChannel.Published[0].Properties.Headers![RabbitMQMessageMapper.ParkReasonHeader]
            .Should().BeOfType<string>()
            .Which.Should().StartWith($"{MessageParkReason.UnreadableEnvelope}:");
        _log.Handled.Should().BeEmpty();
    }

    /// <summary>Builds the delivery the publishing half of this very adapter would have produced.</summary>
    private RabbitMQDelivery Delivery(
        ulong deliveryTag, string orderNumber, Guid? eventId = null, long deliveryCount = 0)
    {
        var serializer = _provider.GetRequiredService<IIntegrationEventSerializer>();
        var mapper = _provider.GetRequiredService<IRabbitMQMessageMapper>();

        var integrationEvent = new SeamOrderPlacedEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            OrderNumber = orderNumber,
        };

        var message = new IntegrationEventMessage
        {
            EventId = eventId ?? Guid.NewGuid(),
            EventName = TestDeliveries.EventName,
            OccurredAt = integrationEvent.OccurredAt,
            Payload = serializer.Serialize(integrationEvent),
        };

        var properties = mapper.ToProperties(message);

        // The client hands string headers back as the bytes they were written as, and the broker adds
        // its redelivery count to the same dictionary, so both go in through that shape.
        var wireHeaders = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties.Headers!)
            wireHeaders[key] = value is string text ? Encoding.UTF8.GetBytes(text) : value;

        if (deliveryCount > 0)
            wireHeaders[RabbitMQMessageMapper.DeliveryCountHeader] = deliveryCount;

        properties.Headers = wireHeaders;

        return new RabbitMQDelivery
        {
            DeliveryTag = deliveryTag,
            RoutingKey = TestDeliveries.EventName,
            IsRedelivered = deliveryCount > 0,
            Properties = properties,
            Body = mapper.ToBody(message),
        };
    }

}
