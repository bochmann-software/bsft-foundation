using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Postgres.Tests.Fakes;
using Bsft.Messaging.EFCore.Postgres.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests;

public sealed class OutboxNotifyInterceptorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RecordingOutboxNotifier _notifier = new();

    public OutboxNotifyInterceptorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private TestOutboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new OutboxNotifyInterceptor(_notifier))
            .Options;

        var context = new TestOutboxDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }

    private static OutboxMessageEntity Message() => new()
    {
        EventId = Guid.NewGuid(),
        EventTypeName = "orders.placed.v1",
        PayloadJson = "{}",
        OccurredAt = new DateTime(2026, 6, 22, 6, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task SavedChangesAsync_ASaveThatWroteOutboxRows_IsAnnounced()
    {
        await using var context = CreateContext();

        context.OutboxMessages.Add(Message());

        await context.SaveChangesAsync();

        _notifier.Asynchronous.Should().ContainSingle().Which.Should().BeSameAs(context);
        _notifier.Synchronous.Should().BeEmpty();
    }

    [Fact]
    public async Task SavedChangesAsync_ASaveWithoutOutboxRows_IsNotAnnounced()
    {
        // The listener is woken for messages, not for saves. A host that writes its own tables through
        // the same context would otherwise pay for a drain pass on every request it serves.
        await using var context = CreateContext();

        await context.SaveChangesAsync();

        _notifier.Total.Should().Be(0);
    }

    [Fact]
    public void SavedChanges_ASynchronousSave_IsAnnouncedToo()
    {
        // A host that saves synchronously installed this package for the same reason as one that does
        // not, and would otherwise get the polling latency back with nothing to say why.
        using var context = CreateContext();

        context.OutboxMessages.Add(Message());

        context.SaveChanges();

        _notifier.Synchronous.Should().ContainSingle().Which.Should().BeSameAs(context);
        _notifier.Asynchronous.Should().BeEmpty();
    }

    [Fact]
    public void SavedChanges_ASynchronousSaveWithoutOutboxRows_IsNotAnnounced()
    {
        using var context = CreateContext();

        context.SaveChanges();

        _notifier.Total.Should().Be(0);
    }

    [Fact]
    public async Task SavedChangesAsync_AnnouncesUnderNoTokenOfTheCallers()
    {
        // The rows are committed by the time the announcement is made, so the caller's token has no say
        // in whether the dispatcher hears about them. Taking it would drop the wake-up for exactly the
        // saves a shutting-down host most wants delivered.
        await using var context = CreateContext();

        context.OutboxMessages.Add(Message());

        using var caller = new CancellationTokenSource();

        await context.SaveChangesAsync(caller.Token);

        _notifier.Tokens.Should().ContainSingle().Which.Should().Be(CancellationToken.None);
    }

    [Fact]
    public void Constructor_RejectsAMissingNotifier()
    {
        var construct = () => new OutboxNotifyInterceptor(null!);

        construct.Should().Throw<ArgumentNullException>();
    }
}
