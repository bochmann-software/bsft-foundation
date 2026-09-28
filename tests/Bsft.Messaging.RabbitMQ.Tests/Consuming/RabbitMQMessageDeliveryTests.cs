using Bsft.Messaging.RabbitMQ.Consuming;
using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests.Consuming;

public class RabbitMQMessageDeliveryTests
{
    private readonly FakeRabbitMQDeliverySettler _settler = new();
    private readonly FakeRabbitMQChannel _channel = new();
    private readonly RabbitMQMessageMapper _mapper = new(new RabbitMQConnectionSettings(TestOptions.Valid()));
    private readonly ImmediateTimeProvider _time = new();

    [Fact]
    public void A_null_dependency_is_rejected()
    {
        var delivery = TestDeliveries.Delivery();

        var withoutSettler = () => new RabbitMQMessageDelivery(null!, _channel, delivery, _mapper, _time);
        var withoutChannel = () => new RabbitMQMessageDelivery(_settler, null!, delivery, _mapper, _time);
        var withoutDelivery = () => new RabbitMQMessageDelivery(_settler, _channel, null!, _mapper, _time);
        var withoutMapper = () => new RabbitMQMessageDelivery(_settler, _channel, delivery, null!, _time);
        var withoutTime = () => new RabbitMQMessageDelivery(_settler, _channel, delivery, _mapper, null!);

        withoutSettler.Should().Throw<ArgumentNullException>();
        withoutChannel.Should().Throw<ArgumentNullException>();
        withoutDelivery.Should().Throw<ArgumentNullException>();
        withoutMapper.Should().Throw<ArgumentNullException>();
        withoutTime.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_delivery_that_carries_no_envelope_cannot_be_presented_at_all()
    {
        // Which is why constructing one is the check: there is nothing to show the host otherwise.
        var present = () => new RabbitMQMessageDelivery(
            _settler, _channel, TestDeliveries.Delivery(messageId: "not-a-guid"), _mapper, _time);

        present.Should().Throw<RabbitMQMessageFormatException>();
    }

    [Fact]
    public void The_envelope_and_the_attempt_are_read_once_when_it_is_built()
    {
        var eventId = Guid.NewGuid();

        var delivery = Delivery(TestDeliveries.Delivery(1, messageId: eventId.ToString()));

        delivery.Message.EventId.Should().Be(eventId);
        delivery.Message.EventName.Should().Be(TestDeliveries.EventName);
        delivery.DeliveryAttempt.Should().Be(1);
    }

    [Fact]
    public void The_attempt_follows_the_brokers_redelivery_count()
    {
        Delivery(TestDeliveries.Redelivery(1, deliveryCount: 5)).DeliveryAttempt.Should().Be(6);
    }

    [Fact]
    public async Task Every_completion_names_the_channel_the_delivery_arrived_on()
    {
        var raw = TestDeliveries.Delivery(4);
        var delivery = Delivery(raw);

        await delivery.AcknowledgeAsync();
        await delivery.RetryAsync(TimeSpan.Zero);
        await delivery.ParkAsync("AttemptsExhausted: enough");

        _settler.Settlements.Select(s => s.Outcome).Should().Equal("acknowledged", "requeued", "parked");
        _settler.Settlements.Should().OnlyContain(s => s.Channel == _channel && s.Delivery == raw);
        _settler.ParkReasons.Should().Equal("AttemptsExhausted: enough");
    }

    [Fact]
    public async Task A_retry_delay_is_waited_out_before_the_message_is_handed_back()
    {
        // AMQP has no delayed requeue, and this adapter will not declare a retry topology on a host's
        // broker as a side effect of a failed message. So the wait is taken here, which is what the
        // contract allows a transport that cannot schedule to do — and what it must document.
        var delivery = Delivery(TestDeliveries.Delivery(7));

        await delivery.RetryAsync(TimeSpan.FromSeconds(30));

        _time.Waits.Should().Equal(TimeSpan.FromSeconds(30));
        _settler.Settlements.Select(s => s.Outcome).Should().Equal("requeued");
    }

    [Fact]
    public async Task A_retry_without_a_delay_costs_no_wait_at_all()
    {
        var delivery = Delivery(TestDeliveries.Delivery(8));

        await delivery.RetryAsync(TimeSpan.Zero);

        _time.Waits.Should().BeEmpty();
        _settler.Settlements.Select(s => s.Outcome).Should().Equal("requeued");
    }

    [Fact]
    public async Task A_park_without_a_reason_is_refused_before_it_reaches_the_transport()
    {
        var park = () => Delivery(TestDeliveries.Delivery()).ParkAsync(null!);

        await park.Should().ThrowAsync<ArgumentNullException>();
        _settler.Settlements.Should().BeEmpty();
    }

    private RabbitMQMessageDelivery Delivery(RabbitMQDelivery raw) =>
        new(_settler, _channel, raw, _mapper, _time);
}
