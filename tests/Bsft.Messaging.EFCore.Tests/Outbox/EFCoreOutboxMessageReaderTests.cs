using Bsft.Data.Contracts.Pagination;
using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Outbox;

/// <summary>
/// The reader an operator lists parked and pending outbox rows through.
/// </summary>
public sealed class EFCoreOutboxMessageReaderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly TestOutboxDbContext _context;
    private readonly EFCoreOutboxMessageReader _reader;

    /// <summary>Opens an in-memory database with the outbox table created.</summary>
    public EFCoreOutboxMessageReaderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestOutboxDbContext>().UseSqlite(_connection).Options;
        _context = new TestOutboxDbContext(options);
        _context.Database.EnsureCreated();
        _reader = new EFCoreOutboxMessageReader(_context);
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
        var act = () => new EFCoreOutboxMessageReader(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    /// <summary>A listing needs a page request.</summary>
    [Fact]
    public async Task ListAsync_WithoutARequest_Throws()
    {
        var act = () => _reader.ListAsync(null, null!);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("request");
    }

    /// <summary>Rows come back newest first, which is the one order this listing has.</summary>
    [Fact]
    public async Task ListAsync_Always_OrdersNewestFirst()
    {
        await SeedAsync(Row(1), Row(2), Row(3));

        var page = await _reader.ListAsync(null, new PagedRequest());

        page.Items.Select(item => item.Id).Should().Equal(3, 2, 1);
        page.TotalCount.Should().Be(3);
        page.HasNextPage.Should().BeFalse();
    }

    /// <summary>The page request is honoured, and the total counts what the filter matched.</summary>
    [Fact]
    public async Task ListAsync_WithAPageRequest_AnswersThatPage()
    {
        await SeedAsync(Row(1), Row(2), Row(3), Row(4), Row(5));

        var page = await _reader.ListAsync(null, new PagedRequest(Page: 2, PageSize: 2));

        page.Items.Select(item => item.Id).Should().Equal(3, 2);
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(2);
        page.TotalCount.Should().Be(5);
        page.HasNextPage.Should().BeTrue();
    }

    /// <summary>A nonsensical page is read as the first one rather than refused.</summary>
    /// <param name="page">The page asked for.</param>
    /// <param name="size">The size asked for.</param>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(-3, 10)]
    [InlineData(1, 0)]
    public async Task ListAsync_WithANonsensicalPage_ReadsTheFirstOne(int page, int size)
    {
        await SeedAsync(Row(1), Row(2));

        var result = await _reader.ListAsync(null, new PagedRequest(Page: page, PageSize: size));

        result.Page.Should().Be(Math.Max(1, page));
        result.PageSize.Should().Be(Math.Max(1, size));
        result.Items.Should().NotBeEmpty();
    }

    /// <summary>The state is read from the timestamps the dispatcher writes.</summary>
    /// <param name="state">The state asked for.</param>
    /// <param name="expectedId">The row that state matches.</param>
    [Theory]
    [InlineData(OutboxMessageState.Pending, 1L)]
    [InlineData(OutboxMessageState.Dispatched, 2L)]
    [InlineData(OutboxMessageState.DeadLettered, 3L)]
    public async Task ListAsync_ByState_MatchesTheRowInIt(OutboxMessageState state, long expectedId)
    {
        await SeedAsync(
            Row(1),
            Row(2, processedAt: Now),
            Row(3, deadLetteredAt: Now, failureReason: "gave up"));

        var page = await _reader.ListAsync(new OutboxMessageFilter(State: state), new PagedRequest());

        page.Items.Should().ContainSingle().Which.Id.Should().Be(expectedId);
    }

    /// <summary>A row that was published after being retired still reads as retired.</summary>
    [Fact]
    public async Task ListAsync_WithARowBothRetiredAndPublished_ReadsItAsRetired()
    {
        await SeedAsync(Row(1, processedAt: Now, deadLetteredAt: Now));

        var page = await _reader.ListAsync(null, new PagedRequest());

        page.Items.Should().ContainSingle().Which.State.Should().Be(OutboxMessageState.DeadLettered);
    }

    /// <summary>An absent state filters nothing.</summary>
    [Fact]
    public async Task ListAsync_WithoutAState_MatchesEverything()
    {
        await SeedAsync(Row(1), Row(2, processedAt: Now));

        var page = await _reader.ListAsync(new OutboxMessageFilter(), new PagedRequest());

        page.TotalCount.Should().Be(2);
    }

    /// <summary>The identity criteria each narrow to their own row.</summary>
    [Fact]
    public async Task ListAsync_ByEventType_MatchesOnlyIt()
    {
        await SeedAsync(Row(1, eventTypeName: "Orders.Placed"), Row(2, eventTypeName: "Billing.Charged"));

        var page = await _reader.ListAsync(
            new OutboxMessageFilter(EventTypeName: "Orders.Placed"), new PagedRequest());

        page.Items.Should().ContainSingle().Which.EventTypeName.Should().Be("Orders.Placed");
    }

    /// <summary>A prefix matches a whole family of events.</summary>
    [Fact]
    public async Task ListAsync_ByEventTypePrefix_MatchesTheFamily()
    {
        await SeedAsync(
            Row(1, eventTypeName: "Orders.Placed"),
            Row(2, eventTypeName: "Orders.Cancelled"),
            Row(3, eventTypeName: "Billing.Charged"));

        var page = await _reader.ListAsync(
            new OutboxMessageFilter(EventTypeNamePrefix: "Orders."), new PagedRequest());

        page.TotalCount.Should().Be(2);
    }

    /// <summary>A prefix carrying a LIKE metacharacter matches itself, not everything.</summary>
    [Fact]
    public async Task ListAsync_ByAPrefixWithAMetacharacter_MatchesItLiterally()
    {
        await SeedAsync(Row(1, eventTypeName: "Orders_Placed"), Row(2, eventTypeName: "OrdersXPlaced"));

        var page = await _reader.ListAsync(
            new OutboxMessageFilter(EventTypeNamePrefix: "Orders_"), new PagedRequest());

        page.Items.Should().ContainSingle().Which.EventTypeName.Should().Be("Orders_Placed");
    }

    /// <summary>An event identifier finds the rows that carry it.</summary>
    [Fact]
    public async Task ListAsync_ByEventId_MatchesOnlyIt()
    {
        var wanted = Guid.NewGuid();
        await SeedAsync(Row(1, eventId: wanted), Row(2));

        var page = await _reader.ListAsync(new OutboxMessageFilter(EventId: wanted), new PagedRequest());

        page.Items.Should().ContainSingle().Which.EventId.Should().Be(wanted);
    }

    /// <summary>A correlation identifier finds everything one request staged.</summary>
    [Fact]
    public async Task ListAsync_ByCorrelationId_MatchesThatRequest()
    {
        await SeedAsync(Row(1, correlationId: "corr-1"), Row(2, correlationId: "corr-2"));

        var page = await _reader.ListAsync(
            new OutboxMessageFilter(CorrelationId: "corr-1"), new PagedRequest());

        page.Items.Should().ContainSingle().Which.CorrelationId.Should().Be("corr-1");
    }

    /// <summary>The time window is inclusive at both ends.</summary>
    [Fact]
    public async Task ListAsync_ByWindow_MatchesWhatFallsInside()
    {
        await SeedAsync(
            Row(1, occurredAt: Now.AddHours(-2)),
            Row(2, occurredAt: Now),
            Row(3, occurredAt: Now.AddHours(2)));

        var page = await _reader.ListAsync(
            new OutboxMessageFilter(
                OccurredAfter: new DateTimeOffset(Now.AddHours(-1)),
                OccurredBefore: new DateTimeOffset(Now.AddHours(1))),
            new PagedRequest());

        page.Items.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    /// <summary>The attempt floor finds what is struggling before it is given up on.</summary>
    [Fact]
    public async Task ListAsync_ByMinimumAttempts_MatchesWhatIsStruggling()
    {
        await SeedAsync(Row(1, attemptCount: 1), Row(2, attemptCount: 6));

        var page = await _reader.ListAsync(new OutboxMessageFilter(MinAttemptCount: 5), new PagedRequest());

        page.Items.Should().ContainSingle().Which.AttemptCount.Should().Be(6);
    }

    /// <summary>A listing row carries the dispatch state and not the payload.</summary>
    [Fact]
    public async Task ListAsync_WithARow_CarriesItsMetadata()
    {
        var eventId = Guid.NewGuid();
        await SeedAsync(Row(
            1,
            eventId: eventId,
            eventTypeName: "Orders.Placed",
            correlationId: "corr-1",
            attemptCount: 3,
            nextAttemptAt: Now.AddMinutes(5),
            leasedUntil: Now.AddMinutes(1),
            deadLetteredAt: Now,
            failureReason: "gave up"));

        var summary = (await _reader.ListAsync(null, new PagedRequest())).Items.Should().ContainSingle().Subject;

        summary.Should().BeEquivalentTo(new OutboxMessageSummary(
            1,
            eventId,
            "Orders.Placed",
            new DateTimeOffset(Now),
            "corr-1",
            OutboxMessageState.DeadLettered,
            3,
            new DateTimeOffset(Now.AddMinutes(5)),
            new DateTimeOffset(Now.AddMinutes(1)),
            null,
            new DateTimeOffset(Now),
            "gave up"));
    }

    /// <summary>The payload comes back only from the read that asks for one row.</summary>
    [Fact]
    public async Task FindAsync_WithARow_AnswersItWithThePayload()
    {
        await SeedAsync(Row(1, payloadJson: """{"orderId":7}"""));

        var view = await _reader.FindAsync(1);

        view!.Summary.Id.Should().Be(1);
        view.PayloadJson.Should().Be("""{"orderId":7}""");
    }

    /// <summary>A row that is not there reads as absent.</summary>
    [Fact]
    public async Task FindAsync_WithoutARow_AnswersNull() => (await _reader.FindAsync(99)).Should().BeNull();

    /// <summary>Writes the given rows.</summary>
    /// <param name="rows">The rows to write.</param>
    /// <returns>A task that completes once they are written.</returns>
    private async Task SeedAsync(params OutboxMessageEntity[] rows)
    {
        _context.OutboxMessages.AddRange(rows);
        await _context.SaveChangesAsync();
    }

    /// <summary>Builds one outbox row.</summary>
    /// <param name="id">The row identifier.</param>
    /// <param name="eventId">The integration event identifier.</param>
    /// <param name="eventTypeName">The event type.</param>
    /// <param name="occurredAt">When it was staged.</param>
    /// <param name="correlationId">The correlating request.</param>
    /// <param name="payloadJson">The serialized event.</param>
    /// <param name="attemptCount">How many attempts were made.</param>
    /// <param name="nextAttemptAt">When the next attempt is due.</param>
    /// <param name="leasedUntil">How long a claim holds.</param>
    /// <param name="processedAt">When it was published.</param>
    /// <param name="deadLetteredAt">When it was retired.</param>
    /// <param name="failureReason">What the last failure said.</param>
    /// <returns>The row.</returns>
    private static OutboxMessageEntity Row(
        long id,
        Guid? eventId = null,
        string eventTypeName = "Orders.Placed",
        DateTime? occurredAt = null,
        string? correlationId = null,
        string payloadJson = "{}",
        int attemptCount = 0,
        DateTime? nextAttemptAt = null,
        DateTime? leasedUntil = null,
        DateTime? processedAt = null,
        DateTime? deadLetteredAt = null,
        string? failureReason = null) => new()
    {
        Id = id,
        EventId = eventId ?? Guid.NewGuid(),
        EventTypeName = eventTypeName,
        OccurredAt = occurredAt ?? Now,
        CorrelationId = correlationId,
        PayloadJson = payloadJson,
        AttemptCount = attemptCount,
        NextAttemptAt = nextAttemptAt,
        LeasedUntil = leasedUntil,
        ProcessedAt = processedAt,
        DeadLetteredAt = deadLetteredAt,
        FailureReason = failureReason
    };
}
