using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client;
using Xunit;
using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.Consuming;

namespace Bsft.Messaging.RabbitMQ.Tests.Consuming;

public class RabbitMQMessageConsumerTests
{
    private readonly BsftRabbitMQOptions _options = TestOptions.Valid();
    private readonly FakeRabbitMQConnectionFactory _connectionFactory = new();
    private readonly FakeRabbitMQParkPublisher _parkPublisher = new();
    private readonly FakeRabbitMQDeliverySink _sink = new();

    [Fact]
    public void A_null_dependency_is_rejected()
    {
        var channelFactory = ChannelFactory();
        var logger = NullLogger<RabbitMQMessageConsumer>.Instance;

        var withoutConnections = () => new RabbitMQMessageConsumer(null!, channelFactory, _parkPublisher, logger);
        var withoutChannels = () => new RabbitMQMessageConsumer(_connectionFactory, null!, _parkPublisher, logger);
        var withoutParking = () => new RabbitMQMessageConsumer(_connectionFactory, channelFactory, null!, logger);
        var withoutLogger = () => new RabbitMQMessageConsumer(_connectionFactory, channelFactory, _parkPublisher, null!);

        withoutConnections.Should().Throw<ArgumentNullException>();
        withoutChannels.Should().Throw<ArgumentNullException>();
        withoutParking.Should().Throw<ArgumentNullException>();
        withoutLogger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_null_sink_is_rejected()
    {
        await using var consumer = Consumer();

        var start = () => consumer.StartAsync(null!);

        await start.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task A_null_argument_is_rejected_by_every_settlement()
    {
        var (channel, consumer) = await StartedAsync();
        await using var _ = consumer;
        var delivery = TestDeliveries.Delivery();

        var acknowledge = () => consumer.AcknowledgeAsync(null!, delivery);
        var requeue = () => consumer.RequeueAsync(channel, null!);
        var parkWithoutChannel = () => consumer.ParkAsync(null!, delivery, "why");
        var parkWithoutDelivery = () => consumer.ParkAsync(channel, null!, "why");
        var parkWithoutReason = () => consumer.ParkAsync(channel, delivery, null!);

        await acknowledge.Should().ThrowAsync<ArgumentNullException>();
        await requeue.Should().ThrowAsync<ArgumentNullException>();
        await parkWithoutChannel.Should().ThrowAsync<ArgumentNullException>();
        await parkWithoutDelivery.Should().ThrowAsync<ArgumentNullException>();
        await parkWithoutReason.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Starting_subscribes_to_the_applications_queue()
    {
        var channel = new FakeRabbitMQChannel();
        _connectionFactory.WithConnection(new FakeRabbitMQConnection().WithChannel(channel));
        await using var consumer = Consumer();

        await consumer.StartAsync(_sink);

        consumer.IsConsuming.Should().BeTrue();
        channel.ConsumedQueue.Should().Be("orders.queue");
    }

    [Fact]
    public async Task Starting_twice_does_not_subscribe_twice()
    {
        await using var consumer = Consumer();

        await consumer.StartAsync(_sink);
        await consumer.StartAsync(_sink);

        _connectionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task A_delivery_reaches_the_sink_together_with_the_channel_it_arrived_on()
    {
        var (channel, consumer) = await StartedAsync();
        await using var _ = consumer;
        var delivery = TestDeliveries.Delivery(7);

        await channel.DeliverAsync(delivery);

        _sink.Received.Should().ContainSingle();
        _sink.Received[0].Channel.Should().BeSameAs(channel);
        _sink.Received[0].Delivery.Should().BeSameAs(delivery);
    }

    [Fact]
    public async Task Nothing_is_decoded_or_dispatched_here_because_that_is_the_hosts_half()
    {
        var (channel, consumer) = await StartedAsync();
        await using var _ = consumer;

        // Unreadable as an envelope, and the consumer neither notices nor cares: moving the bytes is
        // all it does, and what they mean is decided on the other side of the seam.
        await channel.DeliverAsync(TestDeliveries.Delivery(8, messageId: "not-a-guid"));

        _sink.Received.Should().ContainSingle();
        channel.Acked.Should().BeEmpty();
        channel.Nacked.Should().BeEmpty();
    }

    [Fact]
    public async Task An_acknowledged_delivery_is_acknowledged_on_its_own_channel()
    {
        var (channel, consumer) = await StartedAsync();
        await using var _ = consumer;

        await consumer.AcknowledgeAsync(channel, TestDeliveries.Delivery(1));

        channel.Acked.Should().Equal(1UL);
        channel.Nacked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_delivery_the_host_wants_again_is_rejected_with_requeue()
    {
        var (channel, consumer) = await StartedAsync();
        await using var _ = consumer;

        await consumer.RequeueAsync(channel, TestDeliveries.Delivery(3));

        channel.Nacked.Should().Equal((3UL, true));
    }

    [Fact]
    public async Task A_parked_delivery_is_preserved_with_its_reason_before_it_is_acknowledged()
    {
        var (channel, consumer) = await StartedAsync();
        await using var _ = consumer;

        await consumer.ParkAsync(channel, TestDeliveries.Delivery(4), "AttemptsExhausted: the handler kept failing");

        _parkPublisher.Parked.Should().ContainSingle()
            .Which.Reason.Should().Be("AttemptsExhausted: the handler kept failing");
        channel.Acked.Should().Equal(4UL);
    }

    [Fact]
    public async Task A_park_whose_copy_could_not_be_published_falls_back_to_a_rejection()
    {
        // The reason is lost, but the message is not: rejecting without requeue routes it to the same
        // dead-letter exchange through the broker's own dead-lettering.
        _parkPublisher.Preserves = false;
        var (channel, consumer) = await StartedAsync();
        await using var _ = consumer;

        await consumer.ParkAsync(channel, TestDeliveries.Delivery(5), "AttemptsExhausted: still failing");

        channel.Nacked.Should().Equal((5UL, false));
        channel.Acked.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(SettleFailures))]
    public async Task A_channel_that_died_before_the_acknowledgement_does_not_take_the_consumer_down(
        Exception failure)
    {
        // The settlement runs inside the consumer host's loop, which one dead socket may not unwind.
        // The message stays unsettled, which is what the broker redelivers.
        var (channel, consumer) = await StartedAsync();
        await using var _ = consumer;
        channel.SettleFailure = failure;

        var acknowledge = () => consumer.AcknowledgeAsync(channel, TestDeliveries.Delivery(6));

        await acknowledge.Should().NotThrowAsync();
    }

    public static TheoryData<Exception> SettleFailures() =>
    [
        new AlreadyClosedException(
            new global::RabbitMQ.Client.Events.ShutdownEventArgs(ShutdownInitiator.Peer, 320, "connection lost")),
        new IOException("the socket is gone"),
        new TimeoutException("the broker did not answer"),
        new ObjectDisposedException("channel"),
    ];

    [Fact]
    public async Task A_recovered_connection_gets_the_subscription_back_on_a_fresh_channel()
    {
        var first = new FakeRabbitMQChannel();
        var second = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first).WithChannel(second);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);

        await connection.RaiseRecoverySucceededAsync();

        second.ConsumedQueue.Should().Be("orders.queue");
        first.DisposeCount.Should().Be(1);
        consumer.IsConsuming.Should().BeTrue();
    }

    [Fact]
    public async Task The_sink_keeps_receiving_across_a_recovery_so_the_stream_does_not_have_to_end()
    {
        var first = new FakeRabbitMQChannel();
        var second = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first).WithChannel(second);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);
        await connection.RaiseRecoverySucceededAsync();

        await second.DeliverAsync(TestDeliveries.Delivery(10));

        _sink.Received.Should().ContainSingle().Which.Channel.Should().BeSameAs(second);
    }

    [Fact]
    public async Task The_topology_is_declared_again_after_a_recovery_because_the_old_channel_took_it_along()
    {
        _options.Subscribe(TestDeliveries.EventName);
        var first = new FakeRabbitMQChannel();
        var second = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first).WithChannel(second);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);

        await connection.RaiseRecoverySucceededAsync();

        second.Bindings.Should().Contain(("orders.queue", "orders.events", TestDeliveries.EventName));
    }

    [Fact]
    public async Task A_re_subscription_that_fails_is_reported_rather_than_thrown_at_the_client()
    {
        var first = new FakeRabbitMQChannel();
        var broken = new FakeRabbitMQChannel { ConsumeFailure = new OperationInterruptedException() };
        var connection = new FakeRabbitMQConnection().WithChannel(first).WithChannel(broken);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);

        var recover = () => connection.RaiseRecoverySucceededAsync();

        await recover.Should().NotThrowAsync();
        consumer.IsConsuming.Should().BeFalse();
        _sink.Faults.Should().ContainSingle().Which.Should().BeOfType<OperationInterruptedException>();
    }

    [Fact]
    public async Task A_re_subscription_that_fails_on_the_socket_is_reported_rather_than_thrown_at_the_client()
    {
        var first = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);
        connection.CreateChannelFailure = new IOException("the socket is gone");

        var recover = () => connection.RaiseRecoverySucceededAsync();

        await recover.Should().NotThrowAsync();
        _sink.Faults.Should().ContainSingle().Which.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task A_re_subscription_that_times_out_ends_the_stream_rather_than_escaping_the_callback()
    {
        // The client raises a plain TimeoutException when one of its own RPCs runs past the
        // continuation timeout. It is neither a protocol nor a socket failure, and an uncaught one here
        // would unwind into the client's event dispatch instead of reaching the host.
        var first = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);
        connection.CreateChannelFailure = new TimeoutException("the broker did not answer");

        var recover = () => connection.RaiseRecoverySucceededAsync();

        await recover.Should().NotThrowAsync();
        _sink.Faults.Should().ContainSingle().Which.Should().BeOfType<TimeoutException>();
        consumer.IsConsuming.Should().BeFalse();
    }

    [Fact]
    public async Task A_re_subscription_that_succeeds_leaves_the_stream_alone()
    {
        // The whole point of re-subscribing here is that a recovery the client handled does not have to
        // cost the host an enumeration.
        var first = new FakeRabbitMQChannel();
        var second = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first).WithChannel(second);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);

        await connection.RaiseRecoverySucceededAsync();

        _sink.Faults.Should().BeEmpty();
    }

    [Fact]
    public async Task A_delivery_in_flight_when_a_re_subscription_failed_settles_on_nothing()
    {
        // The channel it arrived on is gone, and its delivery tag means something else on whatever
        // channel comes next. Dropping the settlement leaves the message with the broker, which is the
        // outcome at-least-once delivery already accounts for.
        var first = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);
        var delivery = TestDeliveries.Delivery(12);
        connection.CreateChannelFailure = new OperationInterruptedException();
        await connection.RaiseRecoverySucceededAsync();

        await consumer.AcknowledgeAsync(first, delivery);
        await consumer.RequeueAsync(first, delivery);
        await consumer.ParkAsync(first, delivery, "AttemptsExhausted: gone");

        first.Acked.Should().BeEmpty();
        first.Nacked.Should().BeEmpty();
        _parkPublisher.Parked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recovery_after_the_consumer_stopped_is_ignored()
    {
        var connection = new FakeRabbitMQConnection();
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);
        await consumer.StopAsync();

        await connection.RaiseRecoverySucceededAsync();

        consumer.IsConsuming.Should().BeFalse();
    }

    [Fact]
    public async Task Stopping_closes_the_channel_and_the_connection_and_stops_listening_for_recoveries()
    {
        var channel = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(channel);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);

        await consumer.StopAsync();

        channel.DisposeCount.Should().Be(1);
        connection.DisposeCount.Should().Be(1);
        connection.HasRecoverySubscriber.Should().BeFalse();
        consumer.IsConsuming.Should().BeFalse();
    }

    [Fact]
    public async Task A_stopped_consumer_can_be_started_again_which_is_how_a_re_enumerated_stream_reconnects()
    {
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);
        await consumer.StopAsync();

        await consumer.StartAsync(_sink);

        consumer.IsConsuming.Should().BeTrue();
        _connectionFactory.CreateCount.Should().Be(2);
    }

    [Fact]
    public async Task Stopping_a_consumer_that_never_started_does_nothing()
    {
        await using var consumer = Consumer();

        await consumer.StopAsync();

        _connectionFactory.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task Stopping_twice_closes_nothing_twice()
    {
        var channel = new FakeRabbitMQChannel();
        _connectionFactory.WithConnection(new FakeRabbitMQConnection().WithChannel(channel));
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);

        await consumer.StopAsync();
        await consumer.StopAsync();

        channel.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task A_start_that_fails_while_subscribing_leaves_no_open_connection_behind()
    {
        // Otherwise every retry of a failing start would open another connection and attach another
        // recovery handler to it, and nothing would ever close them.
        var channel = new FakeRabbitMQChannel { ConsumeFailure = new OperationInterruptedException() };
        var connection = new FakeRabbitMQConnection().WithChannel(channel);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();

        var start = () => consumer.StartAsync(_sink);

        await start.Should().ThrowAsync<OperationInterruptedException>();
        connection.DisposeCount.Should().Be(1);
        connection.HasRecoverySubscriber.Should().BeFalse();
        consumer.IsConsuming.Should().BeFalse();
    }

    [Fact]
    public async Task A_start_that_cannot_reach_the_broker_leaves_the_consumer_startable_again()
    {
        _connectionFactory.CreateFailure = new BrokerUnreachableException(new IOException("down"));
        await using var consumer = Consumer();
        var firstStart = () => consumer.StartAsync(_sink);
        await firstStart.Should().ThrowAsync<BrokerUnreachableException>();

        _connectionFactory.CreateFailure = null;
        await consumer.StartAsync(_sink);

        consumer.IsConsuming.Should().BeTrue();
    }

    [Fact]
    public async Task A_settlement_for_a_channel_a_recovery_replaced_reaches_neither_channel()
    {
        // A delivery tag only means something on the channel that issued it, and the same number is
        // reused on the next one. Acknowledging a stale delivery against the current channel would
        // acknowledge whichever unrelated message happens to hold that tag there.
        var first = new FakeRabbitMQChannel();
        var second = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first).WithChannel(second);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);
        var delivery = TestDeliveries.Delivery(9);

        await connection.RaiseRecoverySucceededAsync();
        await consumer.AcknowledgeAsync(first, delivery);
        await consumer.RequeueAsync(first, delivery);
        await consumer.ParkAsync(first, delivery, "AttemptsExhausted: gone");

        first.Acked.Should().BeEmpty();
        first.Nacked.Should().BeEmpty();
        second.Acked.Should().BeEmpty();
        second.Nacked.Should().BeEmpty();
        _parkPublisher.Parked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_park_whose_channel_is_replaced_while_the_copy_is_published_settles_nothing()
    {
        // Preserving the message takes a round trip to the broker, and a recovery inside that window
        // makes the delivery tag meaningless. The copy is in the dead-letter queue and the original
        // stays unsettled, which costs a duplicate there rather than an acknowledgement of a stranger.
        var first = new FakeRabbitMQChannel();
        var second = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(first).WithChannel(second);
        _connectionFactory.WithConnection(connection);
        await using var consumer = Consumer();
        await consumer.StartAsync(_sink);
        _parkPublisher.Behaviour = _ => connection.RaiseRecoverySucceededAsync();

        await consumer.ParkAsync(first, TestDeliveries.Delivery(11), "AttemptsExhausted: still failing");

        _parkPublisher.Parked.Should().ContainSingle();
        first.Acked.Should().BeEmpty();
        second.Acked.Should().BeEmpty();
        second.Nacked.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_recovery_callback_already_in_flight_when_the_consumer_stopped_subscribes_nothing(bool disposed)
    {
        // The client reads the handler before it invokes it, so a stop that detaches it can still be
        // followed by one last call. That call must find the consumer stopped and leave it that way.
        var channel = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(channel);
        _connectionFactory.WithConnection(connection);
        var consumer = Consumer();
        await consumer.StartAsync(_sink);
        var inFlight = connection.RecoverySucceeded!;

        if (disposed)
            await consumer.DisposeAsync();
        else
            await consumer.StopAsync();

        await inFlight();

        connection.CreatedChannels.Should().ContainSingle().Which.Should().BeSameAs(channel);
        consumer.IsConsuming.Should().BeFalse();
        _sink.Faults.Should().BeEmpty();
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task Disposal_stops_the_consumer_and_is_idempotent()
    {
        var channel = new FakeRabbitMQChannel();
        _connectionFactory.WithConnection(new FakeRabbitMQConnection().WithChannel(channel));
        var consumer = Consumer();
        await consumer.StartAsync(_sink);

        await consumer.DisposeAsync();
        await consumer.DisposeAsync();

        channel.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task A_disposed_consumer_refuses_to_start()
    {
        var consumer = Consumer();
        await consumer.DisposeAsync();

        var start = () => consumer.StartAsync(_sink);

        await start.Should().ThrowAsync<ObjectDisposedException>();
    }

    private async Task<(FakeRabbitMQChannel Channel, RabbitMQMessageConsumer Consumer)> StartedAsync()
    {
        var channel = new FakeRabbitMQChannel();
        _connectionFactory.WithConnection(new FakeRabbitMQConnection().WithChannel(channel));
        var consumer = Consumer();
        await consumer.StartAsync(_sink);

        return (channel, consumer);
    }

    private RabbitMQMessageConsumer Consumer() =>
        new(_connectionFactory, ChannelFactory(), _parkPublisher, NullLogger<RabbitMQMessageConsumer>.Instance);

    private RabbitMQConsumerChannelFactory ChannelFactory() =>
        new(
            new RabbitMQTopologyDeclarer(new RabbitMQTopology(_options), NullLogger<RabbitMQTopologyDeclarer>.Instance),
            new RabbitMQTopology(_options),
            new RabbitMQConnectionSettings(_options),
            NullLogger<RabbitMQConsumerChannelFactory>.Instance);
}
