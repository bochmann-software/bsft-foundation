using Bsft.Messaging.Consuming;
using Bsft.Messaging.Loopback;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Messaging.Tests.Loopback;

public class LoopbackMessageTransportTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _time = new(Now);

    private LoopbackMessageTransport CreateTransport(
        int? parkedCapacity = null,
        TimeProvider? timeProvider = null,
        ILogger<LoopbackMessageTransport>? logger = null) =>
        new(TestMessagingOptions.For(parkedCapacity is { } capacity
                ? new ConsumerOptions { ConsumerName = "orders", ParkedMessageCapacity = capacity }
                : new ConsumerOptions { ConsumerName = "orders" }),
            timeProvider ?? _time,
            logger ?? NullLogger<LoopbackMessageTransport>.Instance);

    private static IntegrationEventMessage Message(string eventName = "orders.placed.v1") => new()
    {
        EventId = Guid.NewGuid(),
        EventName = eventName,
        OccurredAt = Now,
        Payload = "{}",
    };

    private static async Task<List<IMessageDelivery>> DrainAsync(LoopbackMessageTransport transport)
    {
        transport.Complete();

        var drained = new List<IMessageDelivery>();
        await foreach (var delivery in transport.ReceiveAsync())
            drained.Add(delivery);

        return drained;
    }

    [Fact]
    public async Task A_published_message_comes_back_out_of_the_source()
    {
        var transport = CreateTransport();
        var message = Message();

        await transport.PublishAsync(message);
        var drained = await DrainAsync(transport);

        drained.Should().ContainSingle();
        drained[0].Message.Should().BeSameAs(message);
        drained[0].DeliveryAttempt.Should().Be(1);
    }

    [Fact]
    public async Task Messages_come_back_out_in_the_order_they_were_published()
    {
        var transport = CreateTransport();
        var first = Message("orders.placed.v1");
        var second = Message("orders.shipped.v1");

        await transport.PublishAsync(first);
        await transport.PublishAsync(second);
        var drained = await DrainAsync(transport);

        drained.Select(d => d.Message).Should().ContainInOrder(first, second);
    }

    [Fact]
    public async Task An_acknowledged_message_is_not_delivered_again()
    {
        var transport = CreateTransport();
        await transport.PublishAsync(Message());

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            await stream.Current.AcknowledgeAsync();
        }

        var drained = await DrainAsync(transport);

        drained.Should().BeEmpty();
    }

    [Fact]
    public async Task A_retried_message_comes_back_with_its_attempt_count_raised()
    {
        var transport = CreateTransport();
        await transport.PublishAsync(Message());

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            await stream.Current.RetryAsync(TimeSpan.Zero);
        }

        var drained = await DrainAsync(transport);

        drained.Should().ContainSingle().Which.DeliveryAttempt.Should().Be(2);
    }

    [Fact]
    public async Task A_retried_message_goes_to_the_back_of_the_queue()
    {
        // One message that keeps failing must not starve the ones behind it while it uses up attempts.
        var transport = CreateTransport();
        var failing = Message("orders.placed.v1");
        var following = Message("orders.shipped.v1");
        await transport.PublishAsync(failing);
        await transport.PublishAsync(following);

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            await stream.Current.RetryAsync(TimeSpan.Zero);
        }

        var drained = await DrainAsync(transport);

        drained.Select(d => d.Message).Should().ContainInOrder(following, failing);
    }

    [Fact]
    public async Task A_parked_message_stays_inspectable_instead_of_being_delivered_again()
    {
        var transport = CreateTransport();
        var message = Message();
        await transport.PublishAsync(message);

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            await stream.Current.ParkAsync("UnknownEventName: nobody declared it");
        }

        var drained = await DrainAsync(transport);

        drained.Should().BeEmpty();
        var parked = transport.ParkedMessages.Should().ContainSingle().Subject;
        parked.Message.Should().BeSameAs(message);
        parked.Reason.Should().Be("UnknownEventName: nobody declared it");
        parked.ParkedAt.Should().Be(Now);
    }

    [Fact]
    public void The_parked_list_keeps_only_the_most_recent_hundred_by_default()
    {
        // It lives as long as the process, so a misrouted stream must not become a memory leak.
        var transport = CreateTransport();
        var sink = (ILoopbackMessageSink)transport;

        for (var i = 0; i < 105; i++)
            sink.Park(Message(), $"reason-{i}");

        new ConsumerOptions().ParkedMessageCapacity.Should().Be(100);
        transport.ParkedMessages.Should().HaveCount(100);
        transport.ParkedMessages[^1].Reason.Should().Be("reason-104");
        transport.ParkedMessages[0].Reason.Should().Be("reason-5");
    }

    [Fact]
    public void The_parked_list_is_bounded_by_the_configured_capacity()
    {
        var transport = CreateTransport(parkedCapacity: 3);
        var sink = (ILoopbackMessageSink)transport;

        for (var i = 0; i < 5; i++)
            sink.Park(Message(), $"reason-{i}");

        transport.ParkedMessages.Should().HaveCount(3);
        transport.ParkedMessages.Select(p => p.Reason).Should().Equal("reason-2", "reason-3", "reason-4");
    }

    [Fact]
    public void The_capacity_is_reached_before_anything_is_dropped()
    {
        // The boundary rather than a round number: the capacity is how many are kept, so filling it
        // exactly must still drop nothing and only the one past it may.
        var transport = CreateTransport(parkedCapacity: 3);
        var sink = (ILoopbackMessageSink)transport;

        for (var i = 0; i < 3; i++)
            sink.Park(Message(), $"reason-{i}");

        transport.ParkedMessages.Select(p => p.Reason).Should().Equal("reason-0", "reason-1", "reason-2");

        sink.Park(Message(), "reason-3");

        transport.ParkedMessages.Select(p => p.Reason).Should().Equal("reason-1", "reason-2", "reason-3");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_parked_capacity_that_would_keep_nothing_is_rejected(int capacity)
    {
        // A parked message is the one outcome nothing else is holding, so a bound that drops every one
        // of them turns "never silently dropped" into exactly that.
        var act = () => CreateTransport(parkedCapacity: capacity);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Fact]
    public async Task Retrying_from_inside_the_reader_cannot_block_on_the_queue()
    {
        // The queue is unbounded precisely so this cannot deadlock: the consumer puts a failed message
        // back from inside its own single reader, and a bounded queue would make that write wait for
        // room only the blocked reader could make.
        var transport = CreateTransport();
        await transport.PublishAsync(Message());

        await using var stream = transport.ReceiveAsync().GetAsyncEnumerator();
        await stream.MoveNextAsync();

        var act = async () => await stream.Current.RetryAsync(TimeSpan.Zero);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_null_message_is_rejected()
    {
        var act = async () => await CreateTransport().PublishAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public async Task A_null_park_reason_is_rejected()
    {
        var transport = CreateTransport();
        await transport.PublishAsync(Message());

        await using var stream = transport.ReceiveAsync().GetAsyncEnumerator();
        await stream.MoveNextAsync();

        var act = async () => await stream.Current.ParkAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("reason");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var logger = NullLogger<LoopbackMessageTransport>.Instance;
        var options = TestMessagingOptions.For(new ConsumerOptions { ConsumerName = "orders" });

        var withoutOptions = () => new LoopbackMessageTransport(null!, _time, logger);
        var withoutTime = () => new LoopbackMessageTransport(options, null!, logger);
        var withoutLogger = () => new LoopbackMessageTransport(options, _time, null!);

        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutTime.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void A_delivery_without_its_transport_or_message_is_rejected()
    {
        var transport = CreateTransport();

        var withoutSink = () => new LoopbackDelivery(null!, Message(), 1);
        var withoutMessage = () => new LoopbackDelivery(transport, null!, 1);

        withoutSink.Should().Throw<ArgumentNullException>().WithParameterName("sink");
        withoutMessage.Should().Throw<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public async Task A_retry_with_a_delay_is_scheduled_rather_than_queued_at_once()
    {
        // The whole point of the delay being handed to the transport: the consumer's reader is free
        // again immediately, and the message comes back only once its wait has passed.
        var timers = new ManualTimerProvider();
        var transport = CreateTransport(timeProvider: timers);
        await transport.PublishAsync(Message());

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            await stream.Current.RetryAsync(TimeSpan.FromSeconds(90));
        }

        timers.PendingDelays.Should().Equal(TimeSpan.FromSeconds(90));

        var beforeFiring = await DrainQueuedAsync(transport);
        beforeFiring.Should().BeEmpty();

        timers.FireAll();

        var afterFiring = await DrainAsync(transport);
        afterFiring.Should().ContainSingle().Which.DeliveryAttempt.Should().Be(2);
    }

    [Fact]
    public async Task A_scheduled_redelivery_that_has_fired_leaves_no_timer_behind()
    {
        var timers = new ManualTimerProvider();
        var transport = CreateTransport(timeProvider: timers);
        await transport.PublishAsync(Message());

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            await stream.Current.RetryAsync(TimeSpan.FromSeconds(5));
        }

        timers.FireAll();

        timers.PendingDelays.Should().BeEmpty();
        timers.CreatedTimerCount.Should().Be(1);
    }

    [Fact]
    public async Task A_message_held_back_past_the_end_of_the_transport_is_reported_rather_than_dropped_quietly()
    {
        var timers = new ManualTimerProvider();
        var logger = new RecordingLogger<LoopbackMessageTransport>();
        var transport = CreateTransport(timeProvider: timers, logger: logger);
        await transport.PublishAsync(Message());

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            await stream.Current.RetryAsync(TimeSpan.FromSeconds(5));
        }

        transport.Complete();
        timers.FireAll();

        logger.Warnings.Should().ContainSingle().Which.Should().Contain("closed before it could be delivered again");
    }

    [Fact]
    public async Task Disposing_the_transport_stops_the_redeliveries_that_are_still_waiting()
    {
        var timers = new ManualTimerProvider();
        var transport = CreateTransport(timeProvider: timers);
        await transport.PublishAsync(Message());

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            await stream.Current.RetryAsync(TimeSpan.FromMinutes(5));
        }

        transport.Dispose();
        transport.Dispose();

        timers.PendingDelays.Should().BeEmpty();

        timers.FireAll();

        var drained = await DrainAsync(transport);
        drained.Should().BeEmpty();
    }

    [Fact]
    public async Task A_delay_asked_for_after_the_transport_was_disposed_is_reported_rather_than_scheduled()
    {
        var timers = new ManualTimerProvider();
        var logger = new RecordingLogger<LoopbackMessageTransport>();
        var transport = CreateTransport(timeProvider: timers, logger: logger);
        await transport.PublishAsync(Message());

        IMessageDelivery? held = null;

        await using (var stream = transport.ReceiveAsync().GetAsyncEnumerator())
        {
            await stream.MoveNextAsync();
            held = stream.Current;
        }

        transport.Dispose();

        await held.RetryAsync(TimeSpan.FromSeconds(5));

        timers.PendingDelays.Should().BeEmpty();
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("closed before it could be delivered again");
    }

    /// <summary>
    /// Takes what is on the queue right now, without completing the transport and without waiting for
    /// more.
    /// </summary>
    /// <remarks>
    /// A queued delivery is handed out synchronously, so the first step that does not complete at once
    /// is the stream waiting on an empty queue. Stopping there rather than after a timer means a slow
    /// machine can neither cut the drain short of what is queued nor stretch it.
    /// </remarks>
    /// <param name="transport">The transport to drain.</param>
    /// <returns>The deliveries that were queued.</returns>
    private static async Task<List<IMessageDelivery>> DrainQueuedAsync(LoopbackMessageTransport transport)
    {
        var drained = new List<IMessageDelivery>();

        using var cts = new CancellationTokenSource();
        await using var stream = transport.ReceiveAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var next = stream.MoveNextAsync();

        while (next.IsCompleted && await next)
        {
            drained.Add(stream.Current);
            next = stream.MoveNextAsync();
        }

        if (!next.IsCompleted)
        {
            await cts.CancelAsync();

            // Expected: the stream only ends when the transport is completed, and this drain is asking
            // what is on the queue right now rather than waiting for more.
            var waiting = async () => await next;
            await waiting.Should().ThrowAsync<OperationCanceledException>();
        }

        return drained;
    }
}
