using Bsft.Core.Tenancy;
using Bsft.Messaging.Outbox;
using Bsft.Messaging.Publishing;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using Bsft.Outbox;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Bsft.Messaging.Tests.Outbox;

public class OutboxDispatcherTenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly FakeOutboxStore _store = new();
    private readonly FakeMessageBus _bus = new();
    private readonly FixedTimeProvider _time = new(Now);

    // The clock the wait between passes runs on. It fires every wait at once, so a loop test reaches its
    // next pass without waiting out the poll interval.
    private readonly RecordingTimeProvider _loopClock = new(Now);
    private readonly FakeMessageRetrySchedule _retrySchedule = new();

    private OutboxDispatcher CreateDispatcher(OutboxOptions? options = null)
    {
        var outbox = options ?? new OutboxOptions { PollInterval = TimeSpan.FromMilliseconds(1) };

        return new OutboxDispatcher(
            CreateDrainer(outbox), TestMessagingOptions.For(outbox), _loopClock, NullLogger<OutboxDispatcher>.Instance);
    }

    private OutboxDispatcher CreateDispatcherWithTenants(IBackgroundTenantSource tenants, OutboxOptions? options = null)
    {
        var outbox = options ?? new OutboxOptions { PollInterval = TimeSpan.FromMilliseconds(1) };

        return new OutboxDispatcher(
            CreateDrainer(outbox), TestMessagingOptions.For(outbox), _loopClock, NullLogger<OutboxDispatcher>.Instance, tenants);
    }

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
    public async Task RunLoopAsync_WithATenantSource_WaitsTheIntervalOnTheInjectedClock()
    {
        var interval = TimeSpan.FromMinutes(1);
        var clock = new ManualTimeProvider(Now);
        var outbox = new OutboxOptions { PollInterval = interval };
        var recordedTenants = new List<string?>();
        _store.OnLease = _ =>
        {
            lock (recordedTenants)
                recordedTenants.Add(BackgroundTenant.Current);
        };
        var dispatcher = new OutboxDispatcher(
            CreateDrainer(outbox),
            TestMessagingOptions.For(outbox),
            clock,
            NullLogger<OutboxDispatcher>.Instance,
            new FakeBackgroundTenantSource(["a", "b"]));
        using var stopping = new CancellationTokenSource();

        var loop = dispatcher.RunLoopAsync(stopping.Token);
        await clock.WaitForTimerAsync(interval);
        _store.LeaseCalls.Should().Be(2);

        clock.Advance(interval - TimeSpan.FromTicks(1));
        _store.LeaseCalls.Should().Be(2, "the interval has not passed on the injected clock");

        clock.Advance(TimeSpan.FromTicks(1));
        await clock.WaitForTimerAsync(interval);
        _store.LeaseCalls.Should().Be(4, "the interval passed on the injected clock");
        lock (recordedTenants)
            recordedTenants.Should().Equal("a", "b", "a", "b");

        await stopping.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task ProcessOnceAsync_WithATenantSource_RunsOncePerTenant()
    {
        var recordedTenants = new List<string?>();
        _store.OnLease = _ => recordedTenants.Add(BackgroundTenant.Current);

        var message1 = CreateMessage(id: 1);
        var message2 = CreateMessage(id: 2);
        _store.EnqueueBatch(message1);
        _store.EnqueueBatch(message2);

        var tenants = new FakeBackgroundTenantSource(["a", "b"]);
        var dispatcher = CreateDispatcherWithTenants(tenants);

        var result = await dispatcher.ProcessOnceAsync();

        result.Should().Be(2);
        recordedTenants.Should().Equal(["a", "b"]);
    }

    [Fact]
    public async Task ProcessOnceAsync_WithoutATenantSource_RunsOnceWithoutATenant()
    {
        var recordedTenants = new List<string?>();
        _store.OnLease = _ => recordedTenants.Add(BackgroundTenant.Current);

        var message = CreateMessage();
        _store.EnqueueBatch(message);

        var dispatcher = CreateDispatcher();

        var result = await dispatcher.ProcessOnceAsync();

        result.Should().Be(1);
        recordedTenants.Should().Equal([null]);
    }

    [Fact]
    public void Constructor_WithNullTenants_Throws()
    {
        var options = TestMessagingOptions.For(new OutboxOptions());
        var logger = NullLogger<OutboxDispatcher>.Instance;
        var drainer = CreateDrainer(new OutboxOptions());

        var act = () => new OutboxDispatcher(drainer, options, _loopClock, logger, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenants");
    }
}
