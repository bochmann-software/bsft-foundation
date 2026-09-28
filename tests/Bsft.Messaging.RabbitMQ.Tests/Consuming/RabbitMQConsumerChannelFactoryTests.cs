using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.Consuming;
using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests.Consuming;

public class RabbitMQConsumerChannelFactoryTests
{
    private readonly BsftRabbitMQOptions _options = TestOptions.Valid();

    [Fact]
    public void A_null_dependency_is_rejected()
    {
        var declarer = new RabbitMQTopologyDeclarer(new RabbitMQTopology(_options), NullLogger<RabbitMQTopologyDeclarer>.Instance);
        var topology = new RabbitMQTopology(_options);
        var settings = new RabbitMQConnectionSettings(_options);
        var logger = NullLogger<RabbitMQConsumerChannelFactory>.Instance;

        var withoutDeclarer = () => new RabbitMQConsumerChannelFactory(null!, topology, settings, logger);
        var withoutTopology = () => new RabbitMQConsumerChannelFactory(declarer, null!, settings, logger);
        var withoutSettings = () => new RabbitMQConsumerChannelFactory(declarer, topology, null!, logger);
        var withoutLogger = () => new RabbitMQConsumerChannelFactory(declarer, topology, settings, null!);

        withoutDeclarer.Should().Throw<ArgumentNullException>();
        withoutTopology.Should().Throw<ArgumentNullException>();
        withoutSettings.Should().Throw<ArgumentNullException>();
        withoutLogger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_null_argument_is_rejected()
    {
        var factory = Factory();

        var withoutConnection = () => factory.CreateSubscribedChannelAsync(null!, (_, _) => Task.CompletedTask);
        var withoutCallback = () => factory.CreateSubscribedChannelAsync(new FakeRabbitMQConnection(), null!);

        await withoutConnection.Should().ThrowAsync<ArgumentNullException>();
        await withoutCallback.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task The_channel_is_opened_without_publisher_confirmations_a_consumer_has_no_use_for()
    {
        var connection = new FakeRabbitMQConnection();

        await Factory().CreateSubscribedChannelAsync(connection, (_, _) => Task.CompletedTask);

        connection.RequestedConfirmations.Should().Equal(false);
    }

    [Fact]
    public async Task The_prefetch_comes_from_the_options_and_is_applied_before_the_subscription()
    {
        _options.PrefetchCount = 3;
        var channel = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(channel);

        await Factory().CreateSubscribedChannelAsync(connection, (_, _) => Task.CompletedTask);

        channel.PrefetchCounts.Should().Equal((ushort)3);
        channel.ConsumedQueue.Should().Be("orders.queue");
    }

    [Fact]
    public async Task The_consume_topology_is_declared_before_anything_is_consumed()
    {
        _options.Subscribe("orders.placed.v1");
        var channel = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(channel);

        await Factory().CreateSubscribedChannelAsync(connection, (_, _) => Task.CompletedTask);

        channel.DeclaredQueues.Should().Contain(q => q.Queue == "orders.queue");
        channel.Bindings.Should().Contain(("orders.queue", "orders.events", "orders.placed.v1"));
    }

    [Fact]
    public async Task Deliveries_reach_the_callback_together_with_the_channel_they_arrived_on()
    {
        var received = new List<(IRabbitMQChannel Channel, RabbitMQDelivery Delivery)>();
        var channel = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(channel);
        await Factory().CreateSubscribedChannelAsync(connection, (c, d) =>
        {
            received.Add((c, d));
            return Task.CompletedTask;
        });

        await channel.DeliverAsync(new RabbitMQDelivery { DeliveryTag = 4 });

        var single = received.Should().ContainSingle().Subject;
        single.Delivery.DeliveryTag.Should().Be(4);
        single.Channel.Should().BeSameAs(channel);
    }

    [Fact]
    public async Task A_channel_whose_declaration_failed_is_closed_again()
    {
        var channel = new FakeRabbitMQChannel { DeclareFailure = new OperationInterruptedException() };
        var connection = new FakeRabbitMQConnection().WithChannel(channel);

        var create = () => Factory().CreateSubscribedChannelAsync(connection, (_, _) => Task.CompletedTask);

        await create.Should().ThrowAsync<OperationInterruptedException>();
        channel.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task A_channel_that_never_reached_a_subscription_is_closed_again()
    {
        var channel = new FakeRabbitMQChannel { ConsumeFailure = new OperationInterruptedException() };
        var connection = new FakeRabbitMQConnection().WithChannel(channel);

        var create = () => Factory().CreateSubscribedChannelAsync(connection, (_, _) => Task.CompletedTask);

        await create.Should().ThrowAsync<OperationInterruptedException>();
        channel.DisposeCount.Should().Be(1);
    }

    private RabbitMQConsumerChannelFactory Factory() =>
        new(
            new RabbitMQTopologyDeclarer(new RabbitMQTopology(_options), NullLogger<RabbitMQTopologyDeclarer>.Instance),
            new RabbitMQTopology(_options),
            new RabbitMQConnectionSettings(_options),
            NullLogger<RabbitMQConsumerChannelFactory>.Instance);
}
