using Bsft.Messaging.RabbitMQ.Consuming;
using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client.Exceptions;
using System.Text;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests.Consuming;

public class RabbitMQParkPublisherTests
{
    private readonly BsftRabbitMQOptions _options = TestOptions.Valid();
    private readonly FakeRabbitMQPublishChannelProvider _channelProvider = new();

    [Fact]
    public void A_null_dependency_is_rejected()
    {
        var mapper = Mapper();
        var topology = new RabbitMQTopology(_options);
        var settings = Settings();
        var logger = NullLogger<RabbitMQParkPublisher>.Instance;

        var withoutChannels = () => new RabbitMQParkPublisher(null!, mapper, topology, settings, logger);
        var withoutMapper = () => new RabbitMQParkPublisher(_channelProvider, null!, topology, settings, logger);
        var withoutTopology = () => new RabbitMQParkPublisher(_channelProvider, mapper, null!, settings, logger);
        var withoutSettings = () => new RabbitMQParkPublisher(_channelProvider, mapper, topology, null!, logger);
        var withoutLogger = () => new RabbitMQParkPublisher(_channelProvider, mapper, topology, settings, null!);

        withoutChannels.Should().Throw<ArgumentNullException>();
        withoutMapper.Should().Throw<ArgumentNullException>();
        withoutTopology.Should().Throw<ArgumentNullException>();
        withoutSettings.Should().Throw<ArgumentNullException>();
        withoutLogger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_null_argument_is_rejected()
    {
        var publisher = Publisher();

        var withoutDelivery = () => publisher.TryPublishAsync(null!, "why");
        var withoutReason = () => publisher.TryPublishAsync(TestDeliveries.Delivery(), null!);

        await withoutDelivery.Should().ThrowAsync<ArgumentNullException>();
        await withoutReason.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task The_parked_copy_goes_to_the_dead_letter_exchange_under_its_original_routing_key()
    {
        var preserved = await Publisher().TryPublishAsync(TestDeliveries.Delivery(1), "AttemptsExhausted: broken");

        preserved.Should().BeTrue();
        var published = _channelProvider.Channel.Published.Should().ContainSingle().Subject;
        published.Exchange.Should().Be("orders.events.dlx");
        published.RoutingKey.Should().Be(TestDeliveries.EventName);
    }

    [Fact]
    public async Task The_reason_travels_with_it_as_a_header_so_the_dead_letter_queue_says_why()
    {
        await Publisher().TryPublishAsync(TestDeliveries.Delivery(1), "AttemptsExhausted: the handler kept failing");

        _channelProvider.Channel.Published[0].Properties.Headers
            .Should().ContainKey(RabbitMQMessageMapper.ParkReasonHeader)
            .WhoseValue.Should().Be("AttemptsExhausted: the handler kept failing");
    }

    [Fact]
    public async Task The_body_is_carried_through_byte_for_byte()
    {
        await Publisher().TryPublishAsync(TestDeliveries.Delivery(1, body: """{"id":7}"""), "why");

        Encoding.UTF8.GetString(_channelProvider.Channel.Published[0].Body).Should().Be("""{"id":7}""");
    }

    [Fact]
    public async Task A_body_whose_buffer_was_recycled_after_the_delivery_is_still_parked_as_it_arrived()
    {
        var rented = Encoding.UTF8.GetBytes("""{"id":7}""");
        var delivery = TestDeliveries.Delivery(1, bodyBuffer: rented);

        // What the client does the moment its receive callback returns: the body's array goes back to
        // the pool and the next message is decoded straight into it. Parking is the far end of the
        // delivery and happens long after that, so a delivery that only borrowed the buffer would
        // preserve somebody else's bytes here — and, because the park is confirmed, acknowledge the
        // original away.
        Array.Fill(rented, (byte)'X');

        var preserved = await Publisher().TryPublishAsync(delivery, "AttemptsExhausted: broken");

        preserved.Should().BeTrue();
        Encoding.UTF8.GetString(_channelProvider.Channel.Published[0].Body).Should().Be("""{"id":7}""");
    }

    [Theory]
    [MemberData(nameof(BrokerFailures))]
    public async Task A_broker_that_will_not_take_the_copy_is_reported_rather_than_thrown(Exception failure)
    {
        // The caller still holds an unsettled delivery and rejects it instead, so the message reaches
        // the dead-letter queue anyway; throwing here would unwind the consumer host's loop instead.
        _channelProvider.GetChannelFailure = failure;

        var preserved = await Publisher().TryPublishAsync(TestDeliveries.Delivery(1), "why");

        preserved.Should().BeFalse();
    }

    [Fact]
    public async Task A_publish_that_is_never_confirmed_is_reported_the_same_way()
    {
        _channelProvider.Channel.PublishBehaviour = _ => throw new OperationCanceledException();

        var preserved = await Publisher().TryPublishAsync(TestDeliveries.Delivery(1), "why");

        preserved.Should().BeFalse();
    }

    [Fact]
    public async Task A_broker_that_stops_confirming_is_given_up_on_after_the_confirm_timeout()
    {
        // A broker under a resource alarm keeps the socket alive and simply never confirms. Parking
        // is the terminal step of a strictly sequential consume loop, so a park that waits forever
        // livelocks the whole consumer rather than failing one message.
        _options.PublisherConfirmTimeout = TimeSpan.FromMilliseconds(50);
        _channelProvider.Channel.PublishBehaviour = ct => Task.Delay(Timeout.Infinite, ct);

        var preserved = await Publisher()
            .TryPublishAsync(TestDeliveries.Delivery(1), "why")
            .WaitAsync(TimeSpan.FromSeconds(30));

        preserved.Should().BeFalse();
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_is_not_swallowed_as_a_broker_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _channelProvider.Channel.PublishBehaviour = ct => Task.FromCanceled(ct);

        var publish = () => Publisher().TryPublishAsync(TestDeliveries.Delivery(1), "why", cancelled.Token);

        await publish.Should().ThrowAsync<OperationCanceledException>();
    }

    public static TheoryData<Exception> BrokerFailures() =>
    [
        new OperationInterruptedException(),
        new IOException("the socket is gone"),
        new TimeoutException("the broker did not answer"),

        // A shutdown that reached the publish channel provider before it reached the consumer. It has
        // to be reported like the rest: the caller is a loop that one message may not unwind.
        new ObjectDisposedException("the publish channel provider"),
    ];

    private RabbitMQParkPublisher Publisher() =>
        new(
            _channelProvider,
            Mapper(),
            new RabbitMQTopology(_options),
            Settings(),
            NullLogger<RabbitMQParkPublisher>.Instance);

    private RabbitMQMessageMapper Mapper() => new(Settings());

    private RabbitMQConnectionSettings Settings() => new(_options);
}
