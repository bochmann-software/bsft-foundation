using Bsft.EventAggregator.Saga.EFCore;
using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.EFCore.DependencyInjection;
using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Outbox;
using Bsft.Messaging.Publishing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests;

/// <summary>
/// End-to-end coverage of the guarantee the outbox exists for: an integration event is committed with
/// the state change that raised it, and it is delivered even when the process dies before publishing.
/// </summary>
public sealed class OutboxFlowTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly DeliveryLog _log = new();
    private readonly CorrelationLog _correlationLog = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly ServiceProvider _provider;

    public OutboxFlowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_log);
        services.AddSingleton(_correlationLog);
        services.AddSingleton<TimeProvider>(_time);
        services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, RecordingOrderPlacedHandler>();
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, CorrelationRecordingOrderPlacedHandler>();
        services.AddDbContext<TestOutboxDbContext>((sp, o) => o
            .UseSqlite(_connection)
            .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));
        services.AddBsftMessagingEFCore<TestOutboxDbContext>(o => o.PollInterval = TimeSpan.FromMilliseconds(1));

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// The dispatcher the host runs, resolved rather than composed here, so this end-to-end test
    /// drains through the registration a host actually gets.
    /// </summary>
    private OutboxDispatcher CreateDispatcher() => _provider.GetRequiredService<OutboxDispatcher>();

    private async Task PlaceOrderAsync(
        string orderNumber, bool alsoTouchSaga = false, string? correlationId = null)
    {
        // Stands in for the request pipeline: without Bsft.Messaging.Correlation the ambient scope is
        // the only correlation source, and it is the one the interceptor reads.
        using var correlation = _provider.GetRequiredService<IMessageCorrelationScope>().Use(correlationId);
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        db.Orders.Add(new OrderRow { OrderNumber = orderNumber });

        if (alsoTouchSaga)
        {
            db.SagaStates.Add(new SagaStateEntity
            {
                SagaType = "OrderSaga",
                CorrelationKey = orderNumber,
                StateTypeName = "OrderSagaState",
                StateJson = "{}",
                CurrentStepIndex = 1,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
                ConcurrencyToken = Guid.NewGuid(),
            });
        }

        scope.ServiceProvider.GetRequiredService<IIntegrationEventCollector>()
            .Enqueue(new OrderPlacedEvent { OrderNumber = orderNumber, OccurredAt = Now });

        await db.SaveChangesAsync();
    }

    private async Task<List<OutboxMessageEntity>> ReadOutboxAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        return await db.OutboxMessages.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task The_state_change_and_its_event_are_committed_together_and_delivered_afterwards()
    {
        await PlaceOrderAsync("A-1");

        (await ReadOutboxAsync()).Should().ContainSingle().Which.ProcessedAt.Should().BeNull();
        _log.Entries.Should().BeEmpty("nothing may be published before the transaction is committed");

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(1);
        _log.Entries.Should().ContainSingle().Which.Should().Be("A-1");
        (await ReadOutboxAsync()).Single().ProcessedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task The_correlation_id_of_the_staging_request_reaches_the_handler()
    {
        await PlaceOrderAsync("A-1", correlationId: "request-1");

        (await ReadOutboxAsync()).Should().ContainSingle().Which.CorrelationId.Should().Be("request-1");

        // Nothing of the staging request is alive any more by now: the id survives only because it was
        // committed with the row and put back around the handler.
        await CreateDispatcher().ProcessOnceAsync();

        _correlationLog.Entries.Should().ContainSingle().Which.Should().Be("request-1");
    }

    [Fact]
    public async Task An_event_staged_outside_a_correlated_request_is_delivered_without_an_id()
    {
        await PlaceOrderAsync("A-1");

        (await ReadOutboxAsync()).Should().ContainSingle().Which.CorrelationId.Should().BeNull();

        await CreateDispatcher().ProcessOnceAsync();

        _correlationLog.Entries.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task A_row_written_before_the_correlation_column_existed_still_delivers()
    {
        // Written the way 1.3.0 wrote it: the insert names only the columns that existed then, which is
        // what a row left behind by the previous version looks like once the column has been added.
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

            // Stored the way the provider stores them, so the row reads back through the model exactly
            // as a row written by the previous version does.
            var eventId = Guid.NewGuid().ToString();
            var payload = """{"OrderNumber":"A-1"}""";
            var occurredAt = Now.UtcDateTime;

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO OutboxMessages (EventId, EventTypeName, PayloadJson, OccurredAt, AttemptCount) VALUES ({eventId}, {"orders.placed.v1"}, {payload}, {occurredAt}, 0)");
        }

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(1);
        _log.Entries.Should().ContainSingle().Which.Should().Be("A-1");
        _correlationLog.Entries.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task A_rolled_back_transaction_leaves_neither_the_order_nor_its_event()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();

            db.Orders.Add(new OrderRow { OrderNumber = "A-1" });
            scope.ServiceProvider.GetRequiredService<IIntegrationEventCollector>()
                .Enqueue(new OrderPlacedEvent { OrderNumber = "A-1", OccurredAt = Now });
            await db.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        (await ReadOutboxAsync()).Should().BeEmpty();

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(0);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_survives_a_crash_between_the_commit_and_the_publish()
    {
        // The commit happened; the process dies here, so nothing was published and the lease is stale.
        await PlaceOrderAsync("A-1");
        await _provider.GetRequiredService<IOutboxStore>()
            .LeaseAsync(Now, TimeSpan.FromMinutes(1), 10);

        // The replacement instance starts once the lease has run out.
        _time.Advance(TimeSpan.FromMinutes(2));
        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(1);
        _log.Entries.Should().ContainSingle().Which.Should().Be("A-1");
    }

    [Fact]
    public async Task Saga_state_and_the_events_a_saga_raises_are_written_by_one_save()
    {
        await PlaceOrderAsync("A-1", alsoTouchSaga: true);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        db.SagaStates.AsNoTracking().Should().ContainSingle();
        db.OutboxMessages.AsNoTracking().Should().ContainSingle();
    }

    [Fact]
    public async Task A_message_that_can_never_be_delivered_stops_occupying_the_dispatcher()
    {
        // A payload whose event type was retired: no attempt can ever succeed.
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
            db.OutboxMessages.Add(new OutboxMessageEntity
            {
                EventId = Guid.NewGuid(),
                EventTypeName = "orders.retired.v1",
                PayloadJson = "{}",
                OccurredAt = Now.UtcDateTime,
            });
            await db.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher();
        var options = _provider.GetRequiredService<IOptions<BsftMessagingOptions>>().Value.Outbox;

        for (var attempt = 0; attempt < options.MaxDeliveryAttempts; attempt++)
        {
            await dispatcher.ProcessOnceAsync();

            // Each failure holds the row back, so the next attempt only happens once the wait it was
            // given has passed. Stepping past the ceiling is what makes the loop reach the attempt
            // limit at all — which is the point of the backoff, seen from the other side.
            _time.Advance(options.MaxRetryDelay + TimeSpan.FromSeconds(1));
        }

        var row = (await ReadOutboxAsync()).Single();
        row.DeadLetteredAt.Should().NotBeNull();
        row.AttemptCount.Should().Be(options.MaxDeliveryAttempts);
        row.FailureReason.Should().Contain("orders.retired.v1");

        // Retired, so a later pass leaves it alone instead of retrying it forever.
        (await dispatcher.ProcessOnceAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_second_pass_does_not_deliver_an_already_published_event_again()
    {
        await PlaceOrderAsync("A-1");
        var dispatcher = CreateDispatcher();
        await dispatcher.ProcessOnceAsync();

        var processed = await dispatcher.ProcessOnceAsync();

        processed.Should().Be(0);
        _log.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Every_committed_event_is_delivered_exactly_in_the_order_it_was_committed()
    {
        await PlaceOrderAsync("A-1");
        await PlaceOrderAsync("A-2");

        var processed = await CreateDispatcher().ProcessOnceAsync();

        processed.Should().Be(2);
        _log.Entries.Should().ContainInOrder("A-1", "A-2");
    }
}
