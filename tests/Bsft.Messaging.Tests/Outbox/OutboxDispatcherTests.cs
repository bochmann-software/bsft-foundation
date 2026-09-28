using Bsft.Core.Retry;
using Bsft.Messaging.Outbox;
using Bsft.Messaging.Publishing;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using Bsft.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Bsft.Messaging.Tests.Outbox;

public class OutboxDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly FakeOutboxStore _store = new();
    private readonly FakeMessageBus _bus = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly FakeMessageRetrySchedule _retrySchedule = new();

    private OutboxDispatcher CreateDispatcher(OutboxOptions? options = null)
    {
        var outbox = options ?? new OutboxOptions { PollInterval = TimeSpan.FromMilliseconds(1) };

        return new OutboxDispatcher(
            CreateDrainer(outbox), TestMessagingOptions.For(outbox), NullLogger<OutboxDispatcher>.Instance);
    }

    /// <summary>
    /// The drain pass the host's registration composes, built here from the same three seams so a
    /// dispatcher test still exercises the whole outbox rather than a stand-in for it.
    /// </summary>
    private IOutboxDrainer<IntegrationEventMessage> CreateDrainer(OutboxOptions options) =>
        new OutboxDrainer<IntegrationEventMessage>(
            new MessagingOutboxStore(_store),
            new IntegrationEventPublication(_bus),
            new MessagingOutboxRetrySchedule(_retrySchedule),
            new BsftOutboxOptions
            {
                PollInterval = options.PollInterval,
                BatchSize = options.BatchSize,
                LeaseDuration = options.LeaseDuration,
                MaxDeliveryAttempts = options.MaxDeliveryAttempts,
                RetryBackoff = options.RetryBackoff,
                MaxRetryDelay = options.MaxRetryDelay,
            },
            _time,
            NullLogger<OutboxDispatcher>.Instance);

    private static LeasedOutboxMessage CreateMessage(
        string typeName = "orders.placed.v1",
        string? payloadJson = null,
        long id = 1,
        int attemptCount = 0,
        string? correlationId = null) => new()
    {
        Id = id,
        EventId = Guid.NewGuid(),
        EventTypeName = typeName,
        PayloadJson = payloadJson ?? JsonSerializer.Serialize(new OrderPlacedEvent { OrderNumber = "A-1" }),
        OccurredAt = Now,
        AttemptCount = attemptCount,
        CorrelationId = correlationId,
        LeaseToken = Guid.NewGuid(),
    };

    [Fact]
    public async Task A_leased_message_is_published_and_then_marked_processed()
    {
        var message = CreateMessage();
        _store.EnqueueBatch(message);

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(1);
        _bus.Published.Should().ContainSingle().Which.Payload.Should().Be(message.PayloadJson);
        _store.Marked.Should().ContainSingle().Which.Should().Be((message.Id, message.LeaseToken));
        _store.Abandoned.Should().BeEmpty();
    }

    [Fact]
    public async Task The_wire_message_carries_every_field_the_contract_promises()
    {
        var row = CreateMessage(correlationId: "request-1");
        _store.EnqueueBatch(row);

        await CreateDispatcher().ProcessOnceAsync();

        var published = _bus.Published.Should().ContainSingle().Subject;
        published.MessageVersion.Should().Be(IntegrationEventMessage.CurrentVersion);
        published.EventId.Should().Be(row.EventId);
        published.EventName.Should().Be("orders.placed.v1");
        published.OccurredAt.Should().Be(Now);
        published.CorrelationId.Should().Be("request-1");

        // Carried through byte for byte rather than re-encoded: what a consumer reads has to be what
        // the transaction committed.
        published.Payload.Should().Be(row.PayloadJson);
    }

    [Fact]
    public async Task A_row_staged_without_a_correlation_id_publishes_a_message_without_one()
    {
        // This is what a row written by an earlier version — before the column existed — reads back as,
        // and it still has to go on the wire.
        _store.EnqueueBatch(CreateMessage(correlationId: null));

        await CreateDispatcher().ProcessOnceAsync();

        _bus.Published.Should().ContainSingle().Which.CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task The_processed_instant_is_taken_from_the_time_provider()
    {
        _store.EnqueueBatch(CreateMessage());

        await CreateDispatcher().ProcessOnceAsync();

        _store.LastProcessedInstant.Should().Be(Now);
        _store.LastLeaseInstant.Should().Be(Now);
    }

    [Fact]
    public async Task The_lease_is_taken_with_the_configured_duration_and_batch_size()
    {
        var options = new OutboxOptions { BatchSize = 7, LeaseDuration = TimeSpan.FromSeconds(30) };

        await CreateDispatcher(options).ProcessOnceAsync();

        _store.LastBatchSize.Should().Be(7);
        _store.LastLeaseDuration.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task An_empty_outbox_publishes_nothing()
    {
        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(0);
        _bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_publish_leaves_the_message_in_the_outbox_for_another_attempt()
    {
        var message = CreateMessage();
        _store.EnqueueBatch(message);
        _bus.PublishFailure = new MessagePublishException("transport down");

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(0);
        _store.Marked.Should().BeEmpty();
        _store.Abandoned.Should().ContainSingle().Which.Should()
            .Be((message.Id, message.LeaseToken, Now + _retrySchedule.Delay));
    }

    [Fact]
    public async Task A_message_that_has_used_up_its_attempts_is_retired_with_the_reason_it_failed()
    {
        var message = CreateMessage(attemptCount: 2);
        _store.EnqueueBatch(message);
        _bus.PublishFailure = new MessagePublishException("the consumer rejects this payload");

        var processed = await CreateDispatcher(new OutboxOptions { MaxDeliveryAttempts = 3 }).ProcessOnceAsync();

        processed.Should().Be(0);
        _store.Abandoned.Should().BeEmpty();
        var retired = _store.DeadLettered.Should().ContainSingle().Subject;
        retired.Id.Should().Be(message.Id);
        retired.LeaseToken.Should().Be(message.LeaseToken);
        retired.Reason.Should().Be("the consumer rejects this payload");
        _store.LastDeadLetterInstant.Should().Be(Now);
    }

    [Fact]
    public async Task A_message_with_attempts_left_is_retried_rather_than_retired()
    {
        _store.EnqueueBatch(CreateMessage(attemptCount: 1));
        _bus.PublishFailure = new MessagePublishException("transport down");

        await CreateDispatcher(new OutboxOptions { MaxDeliveryAttempts = 3 }).ProcessOnceAsync();

        _store.Abandoned.Should().ContainSingle();
        _store.DeadLettered.Should().BeEmpty();
    }

    [Fact]
    public async Task A_message_the_bus_refuses_is_not_dropped()
    {
        // Deciding what a name means belongs to the bus now, so a name it cannot route reaches the
        // dispatcher as a failed publish — and a failed publish must leave the row for another attempt.
        var message = CreateMessage("orders.retired.v1");
        _store.EnqueueBatch(message);
        _bus.FailingEventNames.Add("orders.retired.v1");

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(0);
        _bus.Published.Should().BeEmpty();
        _store.Abandoned.Should().ContainSingle();
        _store.Marked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_message_published_under_an_expired_lease_is_reported_but_not_abandoned()
    {
        _store.EnqueueBatch(CreateMessage());
        _store.MarkSucceeds = false;

        var processed = await CreateDispatcher().ProcessOnceAsync();

        // The publish stands; the message belongs to another instance now and may arrive twice.
        processed.Should().Be(0);
        _bus.Published.Should().HaveCount(1);
        _store.Abandoned.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_message_of_a_batch_is_delivered_even_when_one_fails()
    {
        _store.EnqueueBatch(CreateMessage("orders.unknown.v1", id: 1), CreateMessage(id: 2));
        _bus.FailingEventNames.Add("orders.unknown.v1");

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(1);
        _store.Marked.Should().ContainSingle().Which.Id.Should().Be(2);
        _store.Abandoned.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public async Task An_event_that_was_committed_but_never_published_is_delivered_on_a_later_pass()
    {
        // The crash case: the row was committed with the state change, the process died before the
        // publish. The message is still pending, so the next pass delivers it.
        var message = CreateMessage();
        _store.EnqueueBatch(message);
        _bus.PublishFailure = new MessagePublishException("process died");
        var dispatcher = CreateDispatcher();

        await dispatcher.ProcessOnceAsync();

        _bus.PublishFailure = null;
        _store.EnqueueBatch(message);

        var processed = await dispatcher.ProcessOnceAsync();

        processed.Should().Be(1);
        _bus.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task The_loop_keeps_running_after_a_failed_pass()
    {
        _store.LeaseFailure = new InvalidOperationException("the store is unreachable");
        using var cts = new CancellationTokenSource();
        _store.OnLease = calls =>
        {
            if (calls >= 2)
                cts.Cancel();
        };

        await CreateDispatcher().RunLoopAsync(cts.Token);

        _store.LeaseCalls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task The_loop_drains_a_message_and_stops_when_it_is_asked_to()
    {
        _store.EnqueueBatch(CreateMessage());
        using var cts = new CancellationTokenSource();
        _store.OnLease = calls =>
        {
            if (calls >= 2)
                cts.Cancel();
        };

        await CreateDispatcher().RunLoopAsync(cts.Token);

        _bus.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task A_publish_cancelled_by_the_handler_is_a_delivery_failure_and_not_a_shutdown()
    {
        var message = CreateMessage();
        _store.EnqueueBatch(message);
        _bus.PublishTimesOut = true;

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(0);
        _store.Marked.Should().BeEmpty();
        _store.Abandoned.Should().ContainSingle().Which.Should()
            .Be((message.Id, message.LeaseToken, Now + _retrySchedule.Delay));
    }

    [Fact]
    public async Task A_handler_timeout_does_not_stop_the_loop_and_the_message_ends_up_retired()
    {
        _store.EnqueueBatch(CreateMessage());
        _bus.PublishTimesOut = true;
        using var cts = new CancellationTokenSource();
        _store.OnLease = calls =>
        {
            if (calls >= 3)
                cts.Cancel();
        };

        await CreateDispatcher(new OutboxOptions
        {
            MaxDeliveryAttempts = 1,
            PollInterval = TimeSpan.FromMilliseconds(1),
        }).RunLoopAsync(cts.Token);

        // The loop polled on past the timeout instead of ending on the first one, and the message was
        // retired rather than left in the outbox for a dispatcher that is no longer running.
        _store.LeaseCalls.Should().BeGreaterThanOrEqualTo(3);
        _store.DeadLettered.Should().ContainSingle();
    }

    [Fact]
    public async Task A_pass_cancelled_without_a_stop_request_does_not_end_the_loop()
    {
        using var handlerTimeout = new CancellationTokenSource();
        await handlerTimeout.CancelAsync();
        _store.LeaseFailure = new OperationCanceledException("the store call timed out", handlerTimeout.Token);
        using var cts = new CancellationTokenSource();
        _store.OnLease = calls =>
        {
            if (calls >= 2)
                cts.Cancel();
        };

        await CreateDispatcher().RunLoopAsync(cts.Token);

        _store.LeaseCalls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_pass_cancelled_by_the_stop_request_ends_the_loop_without_throwing()
    {
        using var cts = new CancellationTokenSource();
        _store.OnLease = _ => cts.Cancel();
        _store.LeaseFailure = new OperationCanceledException("the host is stopping", cts.Token);

        var act = async () => await CreateDispatcher().RunLoopAsync(cts.Token);

        await act.Should().NotThrowAsync();
        _store.LeaseCalls.Should().Be(1);
    }

    [Fact]
    public async Task The_loop_returns_at_once_when_it_starts_out_cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await CreateDispatcher().RunLoopAsync(cts.Token);

        _store.LeaseCalls.Should().Be(0);
    }

    [Fact]
    public async Task The_hosted_service_entry_point_runs_the_loop_until_the_host_stops_it()
    {
        _store.EnqueueBatch(CreateMessage());
        using var delivered = new SemaphoreSlim(0);
        _store.OnLease = calls =>
        {
            if (calls >= 2)
                delivered.Release();
        };
        var dispatcher = CreateDispatcher(new OutboxOptions { PollInterval = TimeSpan.FromMilliseconds(10) });

        await dispatcher.StartAsync(CancellationToken.None);
        var reached = await delivered.WaitAsync(TimeSpan.FromSeconds(10));
        await dispatcher.StopAsync(CancellationToken.None);

        reached.Should().BeTrue();
        _bus.Published.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_batch_size_that_would_never_claim_anything_is_rejected(int batchSize)
    {
        var act = () => CreateDispatcher(new OutboxOptions { BatchSize = batchSize });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("BatchSize");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_poll_interval_that_would_spin_the_loop_is_rejected(int pollSeconds)
    {
        var act = () => CreateDispatcher(new OutboxOptions { PollInterval = TimeSpan.FromSeconds(pollSeconds) });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("PollInterval");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_attempt_limit_that_would_retire_every_first_failure_is_rejected(int maxAttempts)
    {
        var act = () => CreateDispatcher(new OutboxOptions { MaxDeliveryAttempts = maxAttempts });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxDeliveryAttempts");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_lease_that_expires_immediately_is_rejected(int leaseSeconds)
    {
        var act = () => CreateDispatcher(new OutboxOptions { LeaseDuration = TimeSpan.FromSeconds(leaseSeconds) });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("LeaseDuration");
    }

    [Fact]
    public async Task A_failed_publish_is_held_back_by_the_schedule_rather_than_retried_on_the_next_poll()
    {
        // Without the wait the default ten attempts are spent in under a minute, so a downstream
        // service restarting dead-letters every message in flight before it can come back.
        var message = CreateMessage(attemptCount: 2);
        _store.EnqueueBatch(message);
        _bus.PublishFailure = new MessagePublishException("transport down");
        _retrySchedule.Delay = TimeSpan.FromMinutes(4);

        var options = new OutboxOptions { RetryBackoff = TimeSpan.FromSeconds(20) };
        await CreateDispatcher(options).ProcessOnceAsync();

        _store.Abandoned.Should().ContainSingle().Which.NextAttemptAt.Should().Be(Now.AddMinutes(4));
        _retrySchedule.Requests.Should().ContainSingle().Which.Should()
            .Be((3, TimeSpan.FromSeconds(20), options.MaxRetryDelay, (TimeSpan?)null));
    }

    [Fact]
    public async Task A_handler_that_asks_to_be_tried_later_has_its_wait_carried_into_the_schedule()
    {
        // The 429 of a push service, reaching the outbox through the failure it caused.
        var message = CreateMessage();
        _store.EnqueueBatch(message);
        _bus.PublishFailure = new RetryAfterException("the push service is throttling", TimeSpan.FromMinutes(3));

        await CreateDispatcher().ProcessOnceAsync();

        _retrySchedule.Requests.Should().ContainSingle().Which.RetryAfter.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(-1)]
    public void A_negative_retry_backoff_is_rejected(int backoffSeconds)
    {
        var act = () => CreateDispatcher(new OutboxOptions { RetryBackoff = TimeSpan.FromSeconds(backoffSeconds) });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("RetryBackoff");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_retry_ceiling_that_clamps_every_wait_away_is_rejected(int ceilingSeconds)
    {
        var act = () => CreateDispatcher(new OutboxOptions { MaxRetryDelay = TimeSpan.FromSeconds(ceilingSeconds) });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxRetryDelay");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var options = TestMessagingOptions.For(new OutboxOptions());
        var logger = NullLogger<OutboxDispatcher>.Instance;
        var drainer = CreateDrainer(new OutboxOptions());

        var withoutDrainer = () => new OutboxDispatcher(null!, options, logger);
        var withoutOptions = () => new OutboxDispatcher(drainer, null!, logger);
        var withoutLogger = () => new OutboxDispatcher(drainer, options, null!);

        withoutDrainer.Should().Throw<ArgumentNullException>().WithParameterName("drainer");
        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task A_batch_whose_lease_ran_out_mid_pass_leaves_the_rest_of_it_pending()
    {
        var options = new OutboxOptions { LeaseDuration = TimeSpan.FromSeconds(30) };
        var first = CreateMessage(id: 1);

        _store.EnqueueBatch(first, CreateMessage(id: 2), CreateMessage(id: 3));

        // The first publish alone outlives the lease the whole batch was granted, which is the shape a
        // broker waiting for a publisher confirm produces once the batch is sized too wide for it.
        _bus.OnPublish = _ => _time.Advance(options.LeaseDuration);

        var processed = await CreateDispatcher(options).ProcessOnceAsync();

        processed.Should().Be(1);
        _bus.Published.Should().ContainSingle().Which.EventId.Should().Be(first.EventId);
        _store.Marked.Should().ContainSingle().Which.Should().Be((first.Id, first.LeaseToken));

        // The two left behind keep their pending state: publishing them under a lease this dispatcher
        // no longer holds would emit messages it could not mark, so the next poll claims them instead.
        _store.Abandoned.Should().BeEmpty();
        _store.DeadLettered.Should().BeEmpty();
    }

    [Fact]
    public async Task The_first_message_of_a_batch_is_published_even_when_leasing_it_took_the_whole_lease()
    {
        var options = new OutboxOptions { LeaseDuration = TimeSpan.FromSeconds(30) };
        var first = CreateMessage(id: 1);

        _store.EnqueueBatch(first, CreateMessage(id: 2));

        // Claiming the batch is what overran the lease here, so the deadline is already past when the
        // pass reaches its first message. Skipping that one too would drain nothing, ever.
        _store.OnLease = _ => _time.Advance(options.LeaseDuration + TimeSpan.FromSeconds(1));

        var processed = await CreateDispatcher(options).ProcessOnceAsync();

        processed.Should().Be(1);
        _bus.Published.Should().ContainSingle().Which.EventId.Should().Be(first.EventId);
    }

    [Fact]
    public void The_defaults_poll_gently_and_lease_long_enough_for_a_slow_handler()
    {
        var options = new OutboxOptions();

        options.PollInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.BatchSize.Should().Be(100);
        options.LeaseDuration.Should().Be(TimeSpan.FromMinutes(1));
    }
}
