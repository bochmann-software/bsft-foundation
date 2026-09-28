using Bsft.Messaging.EFCore.DependencyInjection;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Inbox;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Inbox;

public sealed class EFCoreInboxStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public EFCoreInboxStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddBsftInboxEFCore<TestOutboxDbContext>();

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
        Payload = "{}",
    };

    private async Task<List<InboxMessageEntity>> ReadInboxAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        return await db.InboxMessages.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task A_claim_is_staged_and_only_becomes_visible_once_it_is_committed()
    {
        var message = Message();

        using (var scope = _provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();

            (await store.TryBeginAsync(message, "orders", Now)).Should().BeTrue();

            // Staged, not written: nothing has been saved yet, so no other connection can see it.
            (await ReadInboxAsync()).Should().BeEmpty();

            await store.CommitAsync();
        }

        var recorded = (await ReadInboxAsync()).Should().ContainSingle().Subject;
        recorded.EventId.Should().Be(message.EventId);
        recorded.Consumer.Should().Be("orders");
        recorded.EventName.Should().Be("orders.placed.v1");
        recorded.ReceivedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task A_committed_event_is_refused_the_next_time_it_arrives()
    {
        var message = Message();

        using (var scope = _provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            await store.TryBeginAsync(message, "orders", Now);
            await store.CommitAsync();
        }

        using var redelivery = _provider.CreateScope();
        var claimed = await redelivery.ServiceProvider.GetRequiredService<IInboxStore>()
            .TryBeginAsync(message, "orders", Now);

        claimed.Should().BeFalse();
        (await ReadInboxAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task An_abandoned_claim_leaves_nothing_behind_and_the_event_stays_claimable()
    {
        var message = Message();

        using (var scope = _provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            await store.TryBeginAsync(message, "orders", Now);
            await store.AbandonAsync();

            // Detached rather than scheduled for deletion, so even a save that follows writes nothing.
            await scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().SaveChangesAsync();
        }

        (await ReadInboxAsync()).Should().BeEmpty();

        using var redelivery = _provider.CreateScope();
        var claimed = await redelivery.ServiceProvider.GetRequiredService<IInboxStore>()
            .TryBeginAsync(message, "orders", Now);

        claimed.Should().BeTrue();
    }

    [Fact]
    public async Task Two_consumers_of_the_same_event_are_independent_of_each_other()
    {
        var message = Message();

        using (var orders = _provider.CreateScope())
        {
            var store = orders.ServiceProvider.GetRequiredService<IInboxStore>();
            await store.TryBeginAsync(message, "orders", Now);
            await store.CommitAsync();
        }

        using (var invoicing = _provider.CreateScope())
        {
            var store = invoicing.ServiceProvider.GetRequiredService<IInboxStore>();
            (await store.TryBeginAsync(message, "invoicing", Now)).Should().BeTrue();
            await store.CommitAsync();
        }

        (await ReadInboxAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task The_unique_index_settles_a_race_the_read_cannot()
    {
        // Both deliveries read an empty table, so only the database can decide between them. The second
        // commit is refused, which the consumer treats as a failed delivery and retries.
        var message = Message();

        using var first = _provider.CreateScope();
        using var second = _provider.CreateScope();
        var firstStore = first.ServiceProvider.GetRequiredService<IInboxStore>();
        var secondStore = second.ServiceProvider.GetRequiredService<IInboxStore>();

        (await firstStore.TryBeginAsync(message, "orders", Now)).Should().BeTrue();
        (await secondStore.TryBeginAsync(message, "orders", Now)).Should().BeTrue();

        await firstStore.CommitAsync();

        var act = async () => await secondStore.CommitAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        (await ReadInboxAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Committing_without_a_claim_is_refused()
    {
        using var scope = _provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        var act = async () => await store.CommitAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Abandoning_without_a_claim_is_tolerated()
    {
        using var scope = _provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        var act = async () => await store.AbandonAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        using var scope = _provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        var withoutMessage = async () => await store.TryBeginAsync(null!, "orders", Now);
        var withoutConsumer = async () => await store.TryBeginAsync(Message(), null!, Now);

        await withoutMessage.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");
        await withoutConsumer.Should().ThrowAsync<ArgumentNullException>().WithParameterName("consumer");
    }

    [Fact]
    public void A_null_context_is_rejected()
    {
        var act = () => new EFCoreInboxStore(null!, NullLogger<EFCoreInboxStore>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }
}
