using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.EFCore.DependencyInjection;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Inbox;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests;

/// <summary>
/// Two handlers for one event, both writing through the <c>DbContext</c> the inbox mark is staged on.
/// </summary>
/// <remarks>
/// This is the composition the EF Core inbox documents: one scope per delivery, the store and every
/// handler resolved from it, so the mark and the handlers' work commit together. A <c>DbContext</c>
/// runs one operation at a time, so that composition only holds while the handlers do too — an
/// overlapping invocation fails every attempt of the delivery and the event is never handled.
/// </remarks>
public sealed class SharedContextHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public SharedContextHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, ReadingOrderWriter>();
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, SecondReadingOrderWriter>();
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

    [Fact]
    public async Task Two_handlers_sharing_the_delivery_s_context_both_complete()
    {
        var message = new IntegrationEventMessage
        {
            EventId = Guid.NewGuid(),
            EventName = "orders.placed.v1",
            OccurredAt = Now,
            Payload = """{"OrderNumber":"A-1"}""",
        };

        var outcome = await _provider.GetRequiredService<IInboxGuardedDelivery>()
            .DeliverAsync(message, new OrderPlacedEvent { OrderNumber = "A-1" });

        outcome.Should().Be(EventDeliveryOutcome.Handled);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        // Both handlers' rows and the inbox mark, written by the one commit that follows them.
        (await db.Orders.AsNoTracking().ToListAsync()).Select(o => o.OrderNumber)
            .Should().BeEquivalentTo(["A-1 read", "A-1 second"]);
        (await db.InboxMessages.AsNoTracking().ToListAsync()).Should().ContainSingle();
    }
}
