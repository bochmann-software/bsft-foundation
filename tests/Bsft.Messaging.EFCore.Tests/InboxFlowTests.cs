using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.EFCore.DependencyInjection;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Inbox;
using Bsft.Messaging.Publishing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests;

/// <summary>
/// End-to-end coverage of the guarantee the inbox exists for: an event this consumer has already
/// processed never reaches a handler again, and the record that it was processed is committed in the
/// same transaction as the work the handler did.
/// </summary>
public sealed class InboxFlowTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly DeliveryLog _log = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly ServiceProvider _provider;

    public InboxFlowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_log);
        services.AddSingleton<TimeProvider>(_time);
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, OrderWritingHandler>();

        // Before the consumer host, so the durable store wins the TryAdd over the in-memory default.
        services.AddBsftInboxEFCore<TestOutboxDbContext>();
        services.AddBsftConsumerHost(o => o.ConsumerName = "orders");

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static IntegrationEventMessage Message(Guid? eventId = null, string orderNumber = "A-1") => new()
    {
        EventId = eventId ?? Guid.NewGuid(),
        EventName = "orders.placed.v1",
        OccurredAt = Now,
        Payload = $$"""{"OrderNumber":"{{orderNumber}}"}""",
    };

    private Task<EventDeliveryOutcome> DeliverAsync(IntegrationEventMessage message) =>
        _provider.GetRequiredService<IInboxGuardedDelivery>()
            .DeliverAsync(message, new OrderPlacedEvent { OrderNumber = ReadOrderNumber(message) });

    private static string ReadOrderNumber(IntegrationEventMessage message) =>
        System.Text.Json.JsonSerializer.Deserialize<OrderPlacedEvent>(message.Payload)!.OrderNumber;

    private async Task<(List<InboxMessageEntity> Inbox, List<OrderRow> Orders)> ReadAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        return (await db.InboxMessages.AsNoTracking().ToListAsync(),
            await db.Orders.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task The_inbox_mark_and_the_handler_s_write_are_committed_together()
    {
        var outcome = await DeliverAsync(Message());

        outcome.Should().Be(EventDeliveryOutcome.Handled);

        var (inbox, orders) = await ReadAsync();
        inbox.Should().ContainSingle();
        orders.Should().ContainSingle().Which.OrderNumber.Should().Be("A-1");
    }

    [Fact]
    public async Task A_redelivered_event_does_not_reach_the_handler()
    {
        // The acceptance criterion: idempotency is structural, so the handler is not called a second
        // time rather than merely being expected to cope.
        var message = Message();

        await DeliverAsync(message);
        var redelivery = await DeliverAsync(message);

        redelivery.Should().Be(EventDeliveryOutcome.Duplicate);
        _log.Entries.Should().ContainSingle();

        var (inbox, orders) = await ReadAsync();
        inbox.Should().ContainSingle();
        orders.Should().ContainSingle("the handler's write must not happen twice either");
    }

    [Fact]
    public async Task A_different_event_is_handled_even_when_an_earlier_one_was_recorded()
    {
        await DeliverAsync(Message(orderNumber: "A-1"));
        await DeliverAsync(Message(orderNumber: "A-2"));

        var (inbox, orders) = await ReadAsync();
        inbox.Should().HaveCount(2);
        orders.Select(o => o.OrderNumber).Should().BeEquivalentTo(["A-1", "A-2"]);
    }

    [Fact]
    public async Task A_failing_handler_leaves_neither_the_mark_nor_its_own_write()
    {
        // Both are on one context and neither is saved, so the delivery is all-or-nothing and the
        // redelivery is handled rather than swallowed as a duplicate.
        var message = Message(orderNumber: OrderWritingHandler.FailingOrderNumber);

        var act = async () => await DeliverAsync(message);

        await act.Should().ThrowAsync<MessagePublishException>();

        var (inbox, orders) = await ReadAsync();
        inbox.Should().BeEmpty();
        orders.Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_that_failed_once_is_handled_when_it_is_redelivered()
    {
        var failed = Message(orderNumber: OrderWritingHandler.FailingOrderNumber);
        var act = async () => await DeliverAsync(failed);
        await act.Should().ThrowAsync<MessagePublishException>();

        var retry = await DeliverAsync(Message(failed.EventId, "A-1"));

        retry.Should().Be(EventDeliveryOutcome.Handled);
        (await ReadAsync()).Inbox.Should().ContainSingle();
    }

    [Fact]
    public async Task Two_consumers_of_the_same_event_both_handle_it()
    {
        var message = Message();
        await DeliverAsync(message);

        var invoicing = new InboxGuardedDelivery(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<IMessageCorrelationScope>(),
            _provider.GetRequiredService<IIntegrationEventHandlerInvoker>(),
            TestMessagingOptions.For(new ConsumerOptions { ConsumerName = "invoicing" }),
            _time);

        var outcome = await invoicing.DeliverAsync(message, new OrderPlacedEvent { OrderNumber = "A-1" });

        outcome.Should().Be(EventDeliveryOutcome.Handled);
        (await ReadAsync()).Inbox.Should().HaveCount(2);
    }
}
