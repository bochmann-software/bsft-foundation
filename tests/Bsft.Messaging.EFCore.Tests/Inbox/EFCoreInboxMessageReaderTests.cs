using Bsft.Data.Contracts.Pagination;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Inbox;

/// <summary>
/// The reader that answers "was this event ever delivered, and to whom".
/// </summary>
public sealed class EFCoreInboxMessageReaderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly TestOutboxDbContext _context;
    private readonly EFCoreInboxMessageReader _reader;

    /// <summary>Opens an in-memory database with the inbox table created.</summary>
    public EFCoreInboxMessageReaderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestOutboxDbContext>().UseSqlite(_connection).Options;
        _context = new TestOutboxDbContext(options);
        _context.Database.EnsureCreated();
        _reader = new EFCoreInboxMessageReader(_context);
    }

    /// <summary>Closes it again.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>A context is required.</summary>
    [Fact]
    public void Constructor_WithoutAContext_Throws()
    {
        var act = () => new EFCoreInboxMessageReader(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    /// <summary>A listing needs a page request.</summary>
    [Fact]
    public async Task ListAsync_WithoutARequest_Throws()
    {
        var act = () => _reader.ListAsync(null, null!);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("request");
    }

    /// <summary>Rows come back newest first, and carry what the ledger holds.</summary>
    [Fact]
    public async Task ListAsync_Always_OrdersNewestFirstAndCarriesTheRow()
    {
        var eventId = Guid.NewGuid();
        await SeedAsync(Row(1, eventId: eventId), Row(2));

        var page = await _reader.ListAsync(null, new PagedRequest());

        page.Items.Select(item => item.Id).Should().Equal(2, 1);
        page.Items.Last().Should().BeEquivalentTo(
            new InboxMessageSummary(1, eventId, "orders", "Orders.Placed", new DateTimeOffset(Now)));
    }

    /// <summary>An empty filter narrows nothing.</summary>
    [Fact]
    public async Task ListAsync_WithAnEmptyFilter_MatchesEverything()
    {
        await SeedAsync(Row(1), Row(2));

        (await _reader.ListAsync(new InboxMessageFilter(), new PagedRequest())).TotalCount.Should().Be(2);
    }

    /// <summary>Each criterion narrows to its own row.</summary>
    [Fact]
    public async Task ListAsync_ByConsumerAndEventName_MatchesOnlyThem()
    {
        await SeedAsync(
            Row(1, consumer: "orders", eventName: "Orders.Placed"),
            Row(2, consumer: "billing", eventName: "Orders.Placed"),
            Row(3, consumer: "orders", eventName: "Orders.Cancelled"));

        var page = await _reader.ListAsync(
            new InboxMessageFilter(Consumer: "orders", EventName: "Orders.Placed"), new PagedRequest());

        page.Items.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    /// <summary>An event identifier answers "did this one arrive".</summary>
    [Fact]
    public async Task ListAsync_ByEventId_MatchesOnlyIt()
    {
        var wanted = Guid.NewGuid();
        await SeedAsync(Row(1, eventId: wanted), Row(2));

        var page = await _reader.ListAsync(new InboxMessageFilter(EventId: wanted), new PagedRequest());

        page.Items.Should().ContainSingle().Which.EventId.Should().Be(wanted);
    }

    /// <summary>The arrival window is inclusive at both ends.</summary>
    [Fact]
    public async Task ListAsync_ByWindow_MatchesWhatFallsInside()
    {
        await SeedAsync(
            Row(1, receivedAt: Now.AddHours(-2)),
            Row(2, receivedAt: Now),
            Row(3, receivedAt: Now.AddHours(2)));

        var page = await _reader.ListAsync(
            new InboxMessageFilter(
                ReceivedAfter: new DateTimeOffset(Now.AddHours(-1)),
                ReceivedBefore: new DateTimeOffset(Now.AddHours(1))),
            new PagedRequest());

        page.Items.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    /// <summary>Writes the given rows.</summary>
    /// <param name="rows">The rows to write.</param>
    /// <returns>A task that completes once they are written.</returns>
    private async Task SeedAsync(params InboxMessageEntity[] rows)
    {
        _context.InboxMessages.AddRange(rows);
        await _context.SaveChangesAsync();
    }

    /// <summary>Builds one ledger row.</summary>
    /// <param name="id">The row identifier.</param>
    /// <param name="eventId">The integration event identifier.</param>
    /// <param name="consumer">The consumer that handled it.</param>
    /// <param name="eventName">The event type.</param>
    /// <param name="receivedAt">When the delivery was recorded.</param>
    /// <returns>The row.</returns>
    private static InboxMessageEntity Row(
        long id,
        Guid? eventId = null,
        string consumer = "orders",
        string eventName = "Orders.Placed",
        DateTime? receivedAt = null) => new()
    {
        Id = id,
        EventId = eventId ?? Guid.NewGuid(),
        Consumer = consumer,
        EventName = eventName,
        ReceivedAt = receivedAt ?? Now
    };
}
