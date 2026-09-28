using Bsft.Core.Retry;
using Bsft.Messaging.Consuming;
using Bsft.Messaging.Publishing;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Bsft.Messaging.Tests.Consuming;

public class IntegrationEventConsumerTests
{
    private readonly FakeInboxGuardedDelivery _delivery = new();
    private readonly FakeConsumerTelemetry _telemetry = new();
    private readonly IIncomingMessageDecoder _decoder;
    private readonly FakeMessageRetrySchedule _retrySchedule = new();

    public IntegrationEventConsumerTests()
    {
        var options = new BsftMessagingOptions();
        options.AddEvent<OrderPlacedEvent>("orders.placed.v1");
        var registry = new IntegrationEventTypeRegistry(TestMessagingOptions.For(options));
        _decoder = new IncomingMessageDecoder(registry, new JsonIntegrationEventSerializer(registry));
    }

    private IntegrationEventConsumer CreateConsumer(int maxAttempts = 10, TimeSpan? retryDelay = null) =>
        CreateConsumer(_decoder, maxAttempts, retryDelay);

    private IntegrationEventConsumer CreateConsumer(
        IIncomingMessageDecoder decoder, int maxAttempts = 10, TimeSpan? retryDelay = null) =>
        new(decoder,
            _delivery,
            _telemetry,
            _retrySchedule,
            TestMessagingOptions.For(new ConsumerOptions
            {
                ConsumerName = "orders",
                MaxDeliveryAttempts = maxAttempts,
                RetryDelay = retryDelay ?? TimeSpan.Zero,
            }),
            NullLogger<IntegrationEventConsumer>.Instance);

    private static IntegrationEventMessage Message(string eventName = "orders.placed.v1", string? payload = null) => new()
    {
        EventId = Guid.NewGuid(),
        EventName = eventName,
        OccurredAt = DateTimeOffset.UnixEpoch,
        Payload = payload ?? JsonSerializer.Serialize(new OrderPlacedEvent { OrderNumber = "A-1" }),
    };

    [Fact]
    public async Task A_handled_message_is_acknowledged()
    {
        var delivery = new FakeMessageDelivery(Message());

        await CreateConsumer().ConsumeAsync(delivery);

        delivery.Acknowledged.Should().Be(1);
        delivery.Retried.Should().Be(0);
        delivery.ParkReasons.Should().BeEmpty();
        _telemetry.Received.Should().ContainSingle();
        _telemetry.Handled.Should().ContainSingle();
        _telemetry.DeliverySpans.Should().Be(1);
    }

    [Fact]
    public async Task A_redelivery_the_inbox_recognised_is_acknowledged_and_counted_apart()
    {
        var delivery = new FakeMessageDelivery(Message());
        _delivery.Outcome = EventDeliveryOutcome.Duplicate;

        await CreateConsumer().ConsumeAsync(delivery);

        delivery.Acknowledged.Should().Be(1);
        _telemetry.Duplicates.Should().ContainSingle();
        _telemetry.Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_event_name_is_parked_loudly_and_never_dropped()
    {
        var delivery = new FakeMessageDelivery(Message("orders.unknown.v1"));

        await CreateConsumer().ConsumeAsync(delivery);

        delivery.Acknowledged.Should().Be(0);
        delivery.Retried.Should().Be(0);
        delivery.ParkReasons.Should().ContainSingle()
            .Which.Should().Contain(nameof(MessageParkReason.UnknownEventName))
            .And.Contain("orders.unknown.v1");
        _telemetry.Parked.Should().ContainSingle()
            .Which.Reason.Should().Be(MessageParkReason.UnknownEventName);
        _delivery.Deliveries.Should().Be(0);
    }

    [Fact]
    public async Task A_message_that_cannot_be_decoded_is_parked_without_spending_its_attempts()
    {
        // Retrying identical bytes through identical code cannot end differently, so the attempts are
        // not spent on it: it is parked on the first delivery.
        var delivery = new FakeMessageDelivery(Message(payload: "{ not json"));

        await CreateConsumer().ConsumeAsync(delivery);

        delivery.ParkReasons.Should().ContainSingle()
            .Which.Should().Contain(nameof(MessageParkReason.UndecodablePayload));
        delivery.Retried.Should().Be(0);
    }

    [Fact]
    public async Task A_decoder_that_fails_outside_its_contract_still_completes_the_message()
    {
        // Not a refusal it described, so it is treated as an ordinary failed delivery rather than
        // being allowed to escape — a message the consumer neither completes nor leaves for
        // redelivery is the one thing that must never happen.
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 1);

        await CreateConsumer(new FailingIncomingMessageDecoder(), maxAttempts: 5).ConsumeAsync(delivery);

        delivery.Retried.Should().Be(1);
        delivery.Acknowledged.Should().Be(0);
    }

    [Fact]
    public async Task A_decoder_that_keeps_failing_outside_its_contract_ends_up_parked()
    {
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 3);

        await CreateConsumer(new FailingIncomingMessageDecoder(), maxAttempts: 3).ConsumeAsync(delivery);

        delivery.ParkReasons.Should().ContainSingle()
            .Which.Should().Contain(nameof(MessageParkReason.AttemptsExhausted));
    }

    [Fact]
    public async Task A_failing_handler_hands_the_message_back_while_attempts_remain()
    {
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 2);
        _delivery.Failure = new MessagePublishException("the handler failed");

        await CreateConsumer(maxAttempts: 5).ConsumeAsync(delivery);

        delivery.Retried.Should().Be(1);
        delivery.Acknowledged.Should().Be(0);
        delivery.ParkReasons.Should().BeEmpty();
        _telemetry.Retries.Should().ContainSingle();
    }

    [Fact]
    public async Task A_message_that_has_used_up_its_attempts_is_parked_with_the_reason_it_failed()
    {
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 5);
        _delivery.Failure = new MessagePublishException("the handler keeps failing");

        await CreateConsumer(maxAttempts: 5).ConsumeAsync(delivery);

        delivery.Retried.Should().Be(0);
        delivery.ParkReasons.Should().ContainSingle()
            .Which.Should().Contain("the handler keeps failing");
        _telemetry.Parked.Should().ContainSingle()
            .Which.Reason.Should().Be(MessageParkReason.AttemptsExhausted);
    }

    [Fact]
    public async Task A_failure_carrying_several_exceptions_is_reported_by_the_aggregate()
    {
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 1);
        _delivery.Failure = new AggregateException(
            new InvalidOperationException("first"), new InvalidOperationException("second"));

        await CreateConsumer(maxAttempts: 1).ConsumeAsync(delivery);

        delivery.ParkReasons.Should().ContainSingle();
    }

    [Fact]
    public async Task A_delivery_cancelled_by_a_handler_s_own_token_counts_as_a_failure()
    {
        // The HttpClient-timeout shape. Read as a shutdown it would stop the consumer altogether, so
        // it has to become a retry instead.
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 1);
        _delivery.CancelsItself = true;

        await CreateConsumer(maxAttempts: 5).ConsumeAsync(delivery);

        delivery.Retried.Should().Be(1);
        delivery.Acknowledged.Should().Be(0);
    }

    [Fact]
    public async Task A_delivery_cancelled_by_the_shutdown_leaves_the_message_uncompleted()
    {
        var delivery = new FakeMessageDelivery(Message());
        _delivery.BlocksUntilCancelled = true;
        using var cts = new CancellationTokenSource();

        var consuming = CreateConsumer().ConsumeAsync(delivery, cts.Token);
        await cts.CancelAsync();

        var act = async () => await consuming;

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Deliberately not completed: the transport still has it, and whoever is still running gets it.
        delivery.Acknowledged.Should().Be(0);
        delivery.Retried.Should().Be(0);
        delivery.ParkReasons.Should().BeEmpty();
    }

    [Fact]
    public async Task A_transport_that_cannot_accept_the_completion_reports_it()
    {
        var delivery = new FakeMessageDelivery(Message())
        {
            CompletionFailure = new InvalidOperationException("the channel is closed"),
        };

        var act = async () => await CreateConsumer().ConsumeAsync(delivery);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_null_delivery_is_rejected()
    {
        var act = async () => await CreateConsumer().ConsumeAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("delivery");
    }

    [Fact]
    public async Task A_failed_delivery_is_handed_back_with_the_delay_rather_than_the_consumer_sleeping_it_off()
    {
        // The consumer holds one message at a time, so a wait taken here is a wait for every message
        // behind it. The delay travels to the transport, which is free to schedule it.
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 1);
        _delivery.Failure = new MessagePublishException("the handler failed");
        _retrySchedule.Delay = TimeSpan.FromMinutes(2);
        var consumer = CreateConsumer(maxAttempts: 5, retryDelay: TimeSpan.FromSeconds(5));

        // Every collaborator here answers at once, so a consumer that waits nowhere has finished by
        // the time the call returns; a wait of any length would still be running. That makes the
        // check independent of how fast the machine is, where a measured duration is not.
        var consuming = consumer.ConsumeAsync(delivery);
        consuming.IsCompleted.Should().BeTrue("nothing on this path may wait");
        await consuming;

        delivery.Retried.Should().Be(1);
        delivery.RetryDelays.Should().Equal(TimeSpan.FromMinutes(2));
        _retrySchedule.Requests.Should().ContainSingle().Which.Should()
            .Be((1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), (TimeSpan?)null));
    }

    [Fact]
    public async Task A_handler_that_asks_to_be_tried_later_has_its_wait_carried_into_the_schedule()
    {
        // The Retry-After of a throttled channel: it reaches the schedule as the floor it is, rather
        // than being logged and forgotten.
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 2);
        _delivery.Failure = new RetryAfterException("the push service is throttling", TimeSpan.FromSeconds(90));

        await CreateConsumer(maxAttempts: 5).ConsumeAsync(delivery);

        _retrySchedule.Requests.Should().ContainSingle().Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(90));
        delivery.Retried.Should().Be(1);
    }

    [Fact]
    public async Task A_handled_message_is_acknowledged_without_waiting()
    {
        // The delay belongs to the failure, not to the pipeline: a delay long enough to be obvious is
        // configured here and the happy path must not notice it at all.
        var delivery = new FakeMessageDelivery(Message());
        var consumer = CreateConsumer(retryDelay: TimeSpan.FromSeconds(30));

        // Every collaborator here answers at once, so a consumer that waits nowhere has finished by
        // the time the call returns; a wait of any length would still be running. That makes the
        // check independent of how fast the machine is, where a measured duration is not.
        var consuming = consumer.ConsumeAsync(delivery);
        consuming.IsCompleted.Should().BeTrue("nothing on this path may wait");
        await consuming;

        delivery.Acknowledged.Should().Be(1);
    }

    [Fact]
    public async Task A_parked_message_is_not_made_to_wait_for_a_retry_it_will_not_get()
    {
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 5);
        _delivery.Failure = new MessagePublishException("the handler keeps failing");
        var consumer = CreateConsumer(maxAttempts: 5, retryDelay: TimeSpan.FromSeconds(30));

        // Every collaborator here answers at once, so a consumer that waits nowhere has finished by
        // the time the call returns; a wait of any length would still be running. That makes the
        // check independent of how fast the machine is, where a measured duration is not.
        var consuming = consumer.ConsumeAsync(delivery);
        consuming.IsCompleted.Should().BeTrue("nothing on this path may wait");
        await consuming;

        delivery.ParkReasons.Should().ContainSingle();
        _retrySchedule.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_shutdown_reaches_the_transport_that_is_holding_the_delay()
    {
        // The consumer no longer sits the delay out, so a host being stopped is not held by it here.
        // What it does owe the transport is the token: a transport that waits the delay out has to be
        // able to stop waiting when the host is stopping.
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 1);
        _delivery.Failure = new MessagePublishException("the handler failed");
        _retrySchedule.Delay = TimeSpan.FromSeconds(30);
        var consumer = CreateConsumer(maxAttempts: 5, retryDelay: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Every collaborator here answers at once, so a consumer that waits nowhere has finished by
        // the time the call returns; a wait of any length would still be running. That makes the
        // check independent of how fast the machine is, where a measured duration is not.
        var consuming = consumer.ConsumeAsync(delivery, cts.Token);
        consuming.IsCompleted.Should().BeTrue("nothing on this path may wait");
        await consuming;

        delivery.RetryDelays.Should().Equal(TimeSpan.FromSeconds(30));
        delivery.LastRetryToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task The_default_hands_a_failed_message_back_at_once()
    {
        var delivery = new FakeMessageDelivery(Message(), deliveryAttempt: 1);
        _delivery.Failure = new MessagePublishException("the handler failed");

        // Every collaborator here answers at once, so a consumer that waits nowhere has finished by
        // the time the call returns; a wait of any length would still be running. That makes the
        // check independent of how fast the machine is, where a measured duration is not.
        var consuming = CreateConsumer(maxAttempts: 5).ConsumeAsync(delivery);
        consuming.IsCompleted.Should().BeTrue("nothing on this path may wait");
        await consuming;

        new ConsumerOptions().RetryDelay.Should().Be(TimeSpan.Zero);
        delivery.Retried.Should().Be(1);
    }

    [Fact]
    public void A_negative_retry_delay_is_rejected()
    {
        var options = TestMessagingOptions.For(
            new ConsumerOptions { ConsumerName = "orders", RetryDelay = TimeSpan.FromMilliseconds(-1) });

        var act = () => new IntegrationEventConsumer(
            _decoder,
            _delivery,
            _telemetry,
            _retrySchedule,
            options,
            NullLogger<IntegrationEventConsumer>.Instance);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Fact]
    public void A_retry_ceiling_that_clamps_every_wait_away_is_rejected()
    {
        var options = TestMessagingOptions.For(
            new ConsumerOptions { ConsumerName = "orders", MaxRetryDelay = TimeSpan.Zero });

        var act = () => new IntegrationEventConsumer(
            _decoder,
            _delivery,
            _telemetry,
            _retrySchedule,
            options,
            NullLogger<IntegrationEventConsumer>.Instance);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var options = TestMessagingOptions.For(new ConsumerOptions { ConsumerName = "orders" });
        var logger = NullLogger<IntegrationEventConsumer>.Instance;

        var withoutDecoder = () => new IntegrationEventConsumer(
            null!, _delivery, _telemetry, _retrySchedule, options, logger);
        var withoutDelivery = () => new IntegrationEventConsumer(
            _decoder, null!, _telemetry, _retrySchedule, options, logger);
        var withoutTelemetry = () => new IntegrationEventConsumer(
            _decoder, _delivery, null!, _retrySchedule, options, logger);
        var withoutSchedule = () => new IntegrationEventConsumer(
            _decoder, _delivery, _telemetry, null!, options, logger);
        var withoutOptions = () => new IntegrationEventConsumer(
            _decoder, _delivery, _telemetry, _retrySchedule, null!, logger);
        var withoutLogger = () => new IntegrationEventConsumer(
            _decoder, _delivery, _telemetry, _retrySchedule, options, null!);

        withoutDecoder.Should().Throw<ArgumentNullException>().WithParameterName("decoder");
        withoutSchedule.Should().Throw<ArgumentNullException>().WithParameterName("retrySchedule");
        withoutDelivery.Should().Throw<ArgumentNullException>().WithParameterName("delivery");
        withoutTelemetry.Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
