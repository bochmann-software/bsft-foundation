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
/// A delivery whose first handler committed the inbox mark with its own <c>SaveChanges</c> and whose
/// second handler then failed. The mark is real by then, so abandoning has to delete it rather than
/// detach it — otherwise the redelivery the consumer is about to make is refused as a duplicate and
/// the failed handler never runs at all.
/// </summary>
public sealed class InboxAbandonAfterCommitTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public InboxAbandonAfterCommitTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSingleton<DeliveryLog>();
        services.AddSingleton<HandlerAttempts>();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));

        // Registration order is the point of the composition: the first handler needs the key the
        // database generates and so saves at once, which commits the staged inbox mark with it; the
        // second one fails afterwards, when there is nothing left to stage away.
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, EagerlySavingOrderWriter>();
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, FailingOnceOrderWriter>();

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

    private static IntegrationEventMessage Message(Guid? eventId = null) => new()
    {
        EventId = eventId ?? Guid.NewGuid(),
        EventName = "orders.placed.v1",
        OccurredAt = Now,
        Payload = """{"OrderNumber":"A-1"}""",
    };

    private async Task<List<InboxMessageEntity>> ReadInboxAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        return await db.InboxMessages.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task A_redelivery_is_handled_after_a_handler_saved_and_a_later_one_failed()
    {
        var message = Message();
        var integrationEvent = new OrderPlacedEvent { OrderNumber = "A-1" };
        var delivery = _provider.GetRequiredService<IInboxGuardedDelivery>();

        var failed = async () => await delivery.DeliverAsync(message, integrationEvent);

        await failed.Should().ThrowAsync<MessagePublishException>();

        // The mark the first handler's save carried in is gone again, so nothing refuses the retry.
        (await ReadInboxAsync()).Should().BeEmpty();

        var outcome = await delivery.DeliverAsync(message, integrationEvent);

        outcome.Should().Be(EventDeliveryOutcome.Handled);

        // Not merely acknowledged: the handler that failed the first time actually ran again.
        _provider.GetRequiredService<HandlerAttempts>().Count.Should().Be(2);
        (await ReadInboxAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task The_work_the_saving_handler_committed_is_not_undone_by_the_abandon()
    {
        var message = Message();
        var integrationEvent = new OrderPlacedEvent { OrderNumber = "A-1" };
        var delivery = _provider.GetRequiredService<IInboxGuardedDelivery>();

        var failed = async () => await delivery.DeliverAsync(message, integrationEvent);

        await failed.Should().ThrowAsync<MessagePublishException>();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        // Only the mark is compensated. What the handler wrote is its own committed work, and the
        // redelivery meets it again — which is exactly why a handler that saves early must tolerate
        // running twice. Undoing it here is not this store's to do.
        (await db.Orders.AsNoTracking().ToListAsync()).Should().ContainSingle();
    }
}
