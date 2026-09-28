using Bsft.Messaging.EFCore.DependencyInjection;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Inbox;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests;

/// <summary>
/// The store-level half of the same question, without the consumer around it: which rows an abandon
/// touches once the claim has left the staged state, and what happens when the delete cannot run.
/// </summary>
public sealed class InboxAbandonCompensationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public InboxAbandonCompensationTests()
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

    private async Task CommitAsync(IntegrationEventMessage message, string consumer)
    {
        using var scope = _provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        await store.TryBeginAsync(message, consumer, Now);
        await store.CommitAsync();
    }

    private async Task<List<InboxMessageEntity>> ReadInboxAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        return await db.InboxMessages.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task Abandoning_a_committed_claim_deletes_that_row_and_only_that_row()
    {
        var message = Message();
        var otherEvent = Message();

        // Two rows an abandon must not touch: the same consumer's record of a different event, and a
        // different consumer's record of this one. Both are inside the reach of a careless predicate.
        await CommitAsync(otherEvent, "orders");
        await CommitAsync(message, "invoicing");

        using (var scope = _provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

            (await store.TryBeginAsync(message, "orders", Now)).Should().BeTrue();

            // What a handler needing a generated key does, and what the store documents as allowed:
            // its save commits the staged mark along with its own writes.
            await db.SaveChangesAsync();

            (await ReadInboxAsync()).Should().HaveCount(3);

            await store.AbandonAsync();
        }

        var remaining = await ReadInboxAsync();

        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(e => e.EventId == message.EventId && e.Consumer == "orders");

        using var redelivery = _provider.CreateScope();
        var claimed = await redelivery.ServiceProvider.GetRequiredService<IInboxStore>()
            .TryBeginAsync(message, "orders", Now);

        claimed.Should().BeTrue();
    }

    [Fact]
    public async Task Abandoning_a_claim_whose_commit_the_unique_index_refused_leaves_the_winner_alone()
    {
        var message = Message();

        using var first = _provider.CreateScope();
        using var second = _provider.CreateScope();
        var firstStore = first.ServiceProvider.GetRequiredService<IInboxStore>();
        var secondStore = second.ServiceProvider.GetRequiredService<IInboxStore>();

        (await firstStore.TryBeginAsync(message, "orders", Now)).Should().BeTrue();
        (await secondStore.TryBeginAsync(message, "orders", Now)).Should().BeTrue();

        await firstStore.CommitAsync();

        var refused = async () => await secondStore.CommitAsync();

        await refused.Should().ThrowAsync<DbUpdateException>();

        // The loser abandons, as the delivery's finally makes it. The row in the table is the winner's
        // completed mark, not this claim, and a compensating delete here would drop it.
        await secondStore.AbandonAsync();

        (await ReadInboxAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task A_compensating_delete_that_fails_is_reported_and_does_not_replace_the_failure()
    {
        var message = Message();
        var logger = new RecordingLogger<EFCoreInboxStore>();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var store = new EFCoreInboxStore(db, logger);

        await store.TryBeginAsync(message, "orders", Now);
        await db.SaveChangesAsync();

        // The database going away between the handler's commit and the abandon. Nothing can undo the
        // mark then, and the abandon must say so rather than throw over the failure it is cleaning up
        // after.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE InboxMessages");

        var abandon = async () => await store.AbandonAsync();

        await abandon.Should().NotThrowAsync();

        var reported = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        reported.Exception.Should().NotBeNull();
        reported.Message.Should().Contain(message.EventId.ToString()).And.Contain("duplicate");
    }

    [Fact]
    public async Task A_compensated_claim_is_reported_as_the_composition_problem_it_is()
    {
        var message = Message();
        var logger = new RecordingLogger<EFCoreInboxStore>();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var store = new EFCoreInboxStore(db, logger);

        await store.TryBeginAsync(message, "orders", Now);
        await db.SaveChangesAsync();
        await store.AbandonAsync();

        var reported = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        reported.Message.Should().Contain(message.EventId.ToString());
    }

    [Fact]
    public async Task Abandoning_a_staged_claim_says_nothing_because_nothing_happened()
    {
        var message = Message();
        var logger = new RecordingLogger<EFCoreInboxStore>();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var store = new EFCoreInboxStore(db, logger);

        await store.TryBeginAsync(message, "orders", Now);
        await store.AbandonAsync();

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void A_null_logger_is_rejected()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        var act = () => new EFCoreInboxStore(db, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
