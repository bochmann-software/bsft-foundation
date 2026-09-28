using Bsft.Messaging.Consuming;
using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.Consuming;
using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests.Consuming;

public class RabbitMQMessageSourceTests : IAsyncLifetime
{
    private readonly BsftRabbitMQOptions _options = TestOptions.Valid();
    private readonly FakeRabbitMQConnectionFactory _connectionFactory = new();
    private readonly FakeRabbitMQParkPublisher _parkPublisher = new();
    private readonly FakeRabbitMQChannel _channel = new();
    private readonly FakeRabbitMQConnection _connection = new();
    private readonly RabbitMQMessageConsumer _consumer;

    public RabbitMQMessageSourceTests()
    {
        _connection.WithChannel(_channel);
        _connectionFactory.WithConnection(_connection);
        _consumer = new RabbitMQMessageConsumer(
            _connectionFactory, ChannelFactory(), _parkPublisher, NullLogger<RabbitMQMessageConsumer>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _consumer.DisposeAsync();

    [Fact]
    public void A_null_dependency_is_rejected()
    {
        var mapper = Mapper();
        var settings = new RabbitMQConnectionSettings(_options);
        var logger = NullLogger<RabbitMQMessageSource>.Instance;

        var time = TimeProvider.System;

        var withoutConsumer = () => new RabbitMQMessageSource(null!, mapper, settings, time, logger);
        var withoutMapper = () => new RabbitMQMessageSource(_consumer, null!, settings, time, logger);
        var withoutSettings = () => new RabbitMQMessageSource(_consumer, mapper, null!, time, logger);
        var withoutTime = () => new RabbitMQMessageSource(_consumer, mapper, settings, null!, logger);
        var withoutLogger = () => new RabbitMQMessageSource(_consumer, mapper, settings, time, null!);

        withoutConsumer.Should().Throw<ArgumentNullException>();
        withoutMapper.Should().Throw<ArgumentNullException>();
        withoutSettings.Should().Throw<ArgumentNullException>();
        withoutTime.Should().Throw<ArgumentNullException>();
        withoutLogger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_null_argument_is_rejected_by_the_sink()
    {
        var source = Source();

        var withoutChannel = () => source.TakeAsync(null!, TestDeliveries.Delivery());
        var withoutDelivery = () => source.TakeAsync(_channel, null!);
        var withoutFailure = () => source.Fault(null!);

        await withoutChannel.Should().ThrowAsync<ArgumentNullException>();
        await withoutDelivery.Should().ThrowAsync<ArgumentNullException>();
        withoutFailure.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_lost_subscription_ends_the_enumeration_with_the_failure_that_lost_it()
    {
        // This is how a re-subscription the consumer could not complete reaches the host: the stream it
        // is enumerating faults, which is the one thing the host's restart loop reacts to.
        var source = Source();
        var enumerator = source.ReceiveAsync().GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync().AsTask();

        source.Fault(new OperationInterruptedException());

        var next = () => pending;
        await next.Should().ThrowAsync<OperationInterruptedException>();

        // Unwinding the enumeration stops the consumer, so the next enumeration subscribes from
        // scratch rather than reusing a connection whose subscription is gone.
        _consumer.IsConsuming.Should().BeFalse();

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task A_subscription_lost_while_nothing_enumerates_ends_no_stream()
    {
        var source = Source();
        var stream = new SourceStream(source, _channel);
        await stream.DisposeAsync();

        var fault = () => source.Fault(new OperationInterruptedException());

        fault.Should().NotThrow();
    }

    [Fact]
    public async Task A_source_whose_stream_was_faulted_can_be_enumerated_again()
    {
        var source = Source();
        var enumerator = source.ReceiveAsync().GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync().AsTask();
        source.Fault(new OperationInterruptedException());
        var next = () => pending;
        await next.Should().ThrowAsync<OperationInterruptedException>();
        await enumerator.DisposeAsync();

        await using var second = new SourceStream(source, _channel);

        _connectionFactory.CreateCount.Should().Be(2);
        _consumer.IsConsuming.Should().BeTrue();
    }

    [Fact]
    public async Task Enumerating_subscribes_and_a_pushed_message_arrives_as_a_pulled_delivery()
    {
        var eventId = Guid.NewGuid();
        await using var stream = Stream();

        var delivery = await stream.NextAsync(TestDeliveries.Delivery(1, messageId: eventId.ToString()));

        _channel.ConsumedQueue.Should().Be("orders.queue");
        delivery.Message.EventId.Should().Be(eventId);
        delivery.Message.EventName.Should().Be(TestDeliveries.EventName);
    }

    [Fact]
    public async Task A_first_delivery_is_attempt_one_because_the_broker_counts_no_redeliveries_yet()
    {
        await using var stream = Stream();

        var delivery = await stream.NextAsync(TestDeliveries.Delivery(1));

        delivery.DeliveryAttempt.Should().Be(1);
    }

    [Fact]
    public async Task A_redelivery_reports_the_attempt_a_quorum_queue_counted()
    {
        await using var stream = Stream();

        var delivery = await stream.NextAsync(TestDeliveries.Redelivery(1, deliveryCount: 3));

        // Three earlier deliveries of it were never acknowledged, so this one is the fourth attempt.
        delivery.DeliveryAttempt.Should().Be(4);
    }

    [Fact]
    public async Task Acknowledging_a_pulled_delivery_acknowledges_the_tag_on_the_channel_it_arrived_on()
    {
        await using var stream = Stream();
        var delivery = await stream.NextAsync(TestDeliveries.Delivery(12));

        await delivery.AcknowledgeAsync();

        _channel.Acked.Should().Equal(12UL);
    }

    [Fact]
    public async Task Retrying_a_pulled_delivery_hands_it_back_to_the_broker()
    {
        await using var stream = Stream();
        var delivery = await stream.NextAsync(TestDeliveries.Delivery(13));

        await delivery.RetryAsync(TimeSpan.Zero);

        _channel.Nacked.Should().Equal((13UL, true));
    }

    [Fact]
    public async Task Parking_a_pulled_delivery_preserves_it_with_the_hosts_reason()
    {
        await using var stream = Stream();
        var delivery = await stream.NextAsync(TestDeliveries.Delivery(14));

        await delivery.ParkAsync("AttemptsExhausted: the handler kept failing");

        _parkPublisher.Parked.Should().ContainSingle()
            .Which.Reason.Should().Be("AttemptsExhausted: the handler kept failing");
        _channel.Acked.Should().Equal(14UL);
    }

    [Fact]
    public async Task A_pulled_delivery_refuses_a_park_without_a_reason()
    {
        await using var stream = Stream();
        var delivery = await stream.NextAsync(TestDeliveries.Delivery(17));

        var park = () => delivery.ParkAsync(null!);

        await park.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task A_delivery_that_is_not_an_envelope_is_parked_here_and_never_reaches_the_host()
    {
        // There is no IMessageDelivery to hand over for it — an envelope is what one is made of — and a
        // redelivery of the same bytes would be exactly as unreadable, so it is not retried either.
        await using var stream = Stream();

        await _channel.DeliverAsync(TestDeliveries.Delivery(15, messageId: "not-a-guid"));

        // Bound to the enum member rather than to a literal: the leading token of a park reason is a
        // metric dimension elsewhere in the pipeline, so a transport that renamed its own token would
        // put a category into the header that nothing can enumerate.
        _parkPublisher.Parked.Should().ContainSingle()
            .Which.Reason.Should().StartWith($"{MessageParkReason.UnreadableEnvelope}:");
        _channel.Acked.Should().Equal(15UL);
        stream.HasYielded.Should().BeFalse();
    }

    [Fact]
    public async Task Leaving_the_stream_cancels_the_subscription_and_closes_the_connection()
    {
        var stream = Stream();

        await stream.DisposeAsync();

        _consumer.IsConsuming.Should().BeFalse();
        _connection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task A_broker_that_cannot_be_reached_faults_the_stream_so_the_host_retries_it()
    {
        // The reconnect loop belongs to the consumer host, which waits out its restart delay and
        // enumerates again; a source that retried internally would hide an outage the host reports.
        _connectionFactory.CreateFailure = new BrokerUnreachableException(new IOException("down"));

        var enumerate = async () =>
        {
            await foreach (var _ in Source().ReceiveAsync())
            {
                // The stream throws before it yields anything.
            }
        };

        await enumerate.Should().ThrowAsync<BrokerUnreachableException>();
    }

    [Fact]
    public async Task Enumerating_again_after_the_stream_ended_subscribes_again()
    {
        var source = Source();
        var first = new SourceStream(source, _channel);
        await first.DisposeAsync();

        await using var second = new SourceStream(source, _channel);

        _connectionFactory.CreateCount.Should().Be(2);
        _consumer.IsConsuming.Should().BeTrue();
    }

    [Fact]
    public async Task A_delivery_that_arrives_while_nothing_is_enumerating_is_left_for_the_broker()
    {
        var source = Source();
        var stream = new SourceStream(source, _channel);
        await stream.DisposeAsync();

        await source.TakeAsync(_channel, TestDeliveries.Delivery(16));

        _channel.Acked.Should().BeEmpty();
        _channel.Nacked.Should().BeEmpty();
        _parkPublisher.Parked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_delivery_waiting_for_room_when_the_stream_ends_is_left_for_the_broker()
    {
        // The queue holds one delivery; the host has taken the first and not asked for the next, so the
        // second fills the queue and the third waits for room that never comes.
        _options.PrefetchCount = 1;
        var source = Source();
        var enumerator = source.ReceiveAsync().GetAsyncEnumerator();
        var first = enumerator.MoveNextAsync().AsTask();
        await source.TakeAsync(_channel, TestDeliveries.Delivery(1));
        (await first).Should().BeTrue();
        await source.TakeAsync(_channel, TestDeliveries.Delivery(2));

        var waiting = source.TakeAsync(_channel, TestDeliveries.Delivery(3));
        waiting.IsCompleted.Should().BeFalse();

        await enumerator.DisposeAsync();
        var settle = () => waiting;

        await settle.Should().NotThrowAsync();
        _consumer.IsConsuming.Should().BeFalse();
        _channel.Acked.Should().BeEmpty();
        _channel.Nacked.Should().BeEmpty();
        _parkPublisher.Parked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_delivery_cancelled_while_waiting_for_room_is_left_for_the_broker_and_the_stream_goes_on()
    {
        _options.PrefetchCount = 1;
        var second = Guid.NewGuid();
        var source = Source();
        var enumerator = source.ReceiveAsync().GetAsyncEnumerator();
        var first = enumerator.MoveNextAsync().AsTask();
        await source.TakeAsync(_channel, TestDeliveries.Delivery(1));
        (await first).Should().BeTrue();
        await source.TakeAsync(_channel, TestDeliveries.Delivery(2, messageId: second.ToString()));
        using var stopping = new CancellationTokenSource();

        var waiting = source.TakeAsync(_channel, TestDeliveries.Delivery(3), stopping.Token);
        await stopping.CancelAsync();
        var settle = () => waiting;

        await settle.Should().NotThrowAsync();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Message.EventId.Should().Be(second);
        _channel.Acked.Should().BeEmpty();
        _channel.Nacked.Should().BeEmpty();
        _consumer.IsConsuming.Should().BeTrue();

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task A_host_that_stops_pulling_after_a_delivery_closes_the_subscription()
    {
        var source = Source();

        var consumed = 0;
        var pulling = Task.Run(async () =>
        {
            await foreach (var _ in source.ReceiveAsync())
            {
                consumed++;
                break;
            }
        });
        await Eventually.HoldsAsync(() => _consumer.IsConsuming);
        await _channel.DeliverAsync(TestDeliveries.Delivery(1));
        await pulling;

        consumed.Should().Be(1);
        _consumer.IsConsuming.Should().BeFalse();
        _connection.DisposeCount.Should().Be(1);
    }

    private SourceStream Stream() => new(Source(), _channel);

    private RabbitMQMessageSource Source() =>
        new(
            _consumer,
            Mapper(),
            new RabbitMQConnectionSettings(_options),
            TimeProvider.System,
            NullLogger<RabbitMQMessageSource>.Instance);

    private RabbitMQMessageMapper Mapper() => new(new RabbitMQConnectionSettings(_options));

    private RabbitMQConsumerChannelFactory ChannelFactory() =>
        new(
            new RabbitMQTopologyDeclarer(new RabbitMQTopology(_options), NullLogger<RabbitMQTopologyDeclarer>.Instance),
            new RabbitMQTopology(_options),
            new RabbitMQConnectionSettings(_options),
            NullLogger<RabbitMQConsumerChannelFactory>.Instance);

    /// <summary>
    /// One enumeration of the source, held open with an outstanding request for the next delivery —
    /// which is exactly how the consumer host holds it.
    /// </summary>
    private sealed class SourceStream : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly IAsyncEnumerator<IMessageDelivery> _enumerator;
        private readonly FakeRabbitMQChannel _channel;
        private Task<bool> _pending;

        public SourceStream(RabbitMQMessageSource source, FakeRabbitMQChannel channel)
        {
            _channel = channel;
            _enumerator = source.ReceiveAsync(_stopping.Token).GetAsyncEnumerator();
            _pending = _enumerator.MoveNextAsync().AsTask();
        }

        /// <summary>Gets whether the outstanding request has produced a delivery.</summary>
        public bool HasYielded => _pending.IsCompletedSuccessfully;

        /// <summary>Pushes a delivery through the subscription and returns what the stream yielded.</summary>
        public async Task<IMessageDelivery> NextAsync(RabbitMQDelivery delivery)
        {
            await _channel.DeliverAsync(delivery);
            (await _pending).Should().BeTrue();

            var current = _enumerator.Current;
            _pending = _enumerator.MoveNextAsync().AsTask();

            return current;
        }

        public async ValueTask DisposeAsync()
        {
            // Cancelled rather than disposed first: an async iterator cannot be disposed while a
            // request for its next element is still outstanding, and cancelling is how the consumer
            // host ends one too.
            await _stopping.CancelAsync();

            try
            {
                await _pending;
            }
            catch (OperationCanceledException)
            {
                // The stream ending on the token it was given is the point of cancelling it.
            }

            await _enumerator.DisposeAsync();
            _stopping.Dispose();
        }
    }
}
