using Bsft.EventAggregator.Saga.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests;

/// <summary>
/// The timeouts as they hang off the instance an operator is looking at.
/// </summary>
public sealed class EFCoreSagaTimeoutReaderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly TestSagaDbContext _context;
    private readonly EFCoreSagaTimeoutReader _reader;

    /// <summary>Opens an in-memory database with the saga tables created.</summary>
    public EFCoreSagaTimeoutReaderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(_connection).Options;
        _context = new TestSagaDbContext(options);
        _context.Database.EnsureCreated();
        _reader = new EFCoreSagaTimeoutReader(_context);
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
        var act = () => new EFCoreSagaTimeoutReader(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    /// <summary>Both halves of the instance's identity are required.</summary>
    [Fact]
    public async Task ListForAsync_WithoutASagaType_Throws()
    {
        var act = () => _reader.ListForAsync(null!, "order-1");

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("sagaType");
    }

    /// <summary>Both halves of the instance's identity are required.</summary>
    [Fact]
    public async Task ListForAsync_WithoutACorrelationKey_Throws()
    {
        var act = () => _reader.ListForAsync("Orders.Fulfil", null!);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("correlationKey");
    }

    /// <summary>Only the instance's own timeouts come back, soonest due first.</summary>
    [Fact]
    public async Task ListForAsync_WithTimeouts_AnswersItsOwnInDueOrder()
    {
        await SeedAsync(
            Row("later", dueAt: Now.AddHours(2)),
            Row("sooner", dueAt: Now.AddHours(1)),
            Row("elsewhere", correlationKey: "order-2"));

        var timeouts = await _reader.ListForAsync("Orders.Fulfil", "order-1");

        timeouts.Select(timeout => timeout.Name).Should().Equal("sooner", "later");
    }

    /// <summary>
    /// A retired timeout is on the list too: it is the most interesting row an operator can see there.
    /// </summary>
    [Fact]
    public async Task ListForAsync_WithARetiredTimeout_AnswersItToo()
    {
        await SeedAsync(Row(
            "gave-up",
            attemptCount: 5,
            dispatchedAt: Now,
            deadLetteredAt: Now,
            deadLetterReason: "handler threw"));

        var timeout = (await _reader.ListForAsync("Orders.Fulfil", "order-1")).Should().ContainSingle().Subject;

        timeout.AttemptCount.Should().Be(5);
        timeout.DispatchedAt.Should().Be(new DateTimeOffset(Now));
        timeout.DeadLetteredAt.Should().Be(new DateTimeOffset(Now));
        timeout.DeadLetterReason.Should().Be("handler threw");
    }

    /// <summary>An instance waiting on nothing answers an empty list.</summary>
    [Fact]
    public async Task ListForAsync_WithoutTimeouts_AnswersEmpty() =>
        (await _reader.ListForAsync("Orders.Fulfil", "order-1")).Should().BeEmpty();

    /// <summary>A timeout carries what it will publish, which is why it is a content read.</summary>
    [Fact]
    public async Task ListForAsync_WithATimeout_CarriesItsPayload()
    {
        await SeedAsync(Row(
            "reminder",
            stepIndex: 2,
            payloadJson: """{"orderId":7}""",
            payloadTypeName: "Orders.Reminder, Orders",
            correlationId: "corr-1"));

        var timeout = (await _reader.ListForAsync("Orders.Fulfil", "order-1")).Should().ContainSingle().Subject;

        timeout.Id.Should().BeGreaterThan(0);
        timeout.StepIndex.Should().Be(2);
        timeout.DueAt.Should().Be(new DateTimeOffset(Now.AddHours(1)));
        timeout.PayloadJson.Should().Be("""{"orderId":7}""");
        timeout.PayloadTypeName.Should().Be("Orders.Reminder, Orders");
        timeout.CorrelationId.Should().Be("corr-1");
        timeout.DispatchedAt.Should().BeNull();
        timeout.DeadLetteredAt.Should().BeNull();
        timeout.DeadLetterReason.Should().BeNull();
    }

    /// <summary>Writes the given rows.</summary>
    /// <param name="rows">The rows to write.</param>
    /// <returns>A task that completes once they are written.</returns>
    private async Task SeedAsync(params SagaTimeoutEntity[] rows)
    {
        _context.SagaTimeouts.AddRange(rows);
        await _context.SaveChangesAsync();
    }

    /// <summary>Builds one timeout row.</summary>
    /// <param name="name">The timeout's name.</param>
    /// <param name="sagaType">The saga definition.</param>
    /// <param name="correlationKey">What identifies the instance.</param>
    /// <param name="stepIndex">The waiting step.</param>
    /// <param name="dueAt">When it fires.</param>
    /// <param name="payloadJson">The serialized event.</param>
    /// <param name="payloadTypeName">The event type.</param>
    /// <param name="correlationId">The captured correlation.</param>
    /// <param name="attemptCount">How many dispatches failed.</param>
    /// <param name="dispatchedAt">When it left the pending set.</param>
    /// <param name="deadLetteredAt">When it was retired.</param>
    /// <param name="deadLetterReason">What retired it.</param>
    /// <returns>The row.</returns>
    private static SagaTimeoutEntity Row(
        string name,
        string sagaType = "Orders.Fulfil",
        string correlationKey = "order-1",
        int stepIndex = 0,
        DateTime? dueAt = null,
        string payloadJson = "{}",
        string payloadTypeName = "Orders.Reminder, Orders",
        string? correlationId = null,
        int attemptCount = 0,
        DateTime? dispatchedAt = null,
        DateTime? deadLetteredAt = null,
        string? deadLetterReason = null) => new()
    {
        SagaType = sagaType,
        CorrelationKey = correlationKey,
        Name = name,
        StepIndex = stepIndex,
        DueAt = dueAt ?? Now.AddHours(1),
        PayloadJson = payloadJson,
        PayloadTypeName = payloadTypeName,
        CorrelationId = correlationId,
        AttemptCount = attemptCount,
        DispatchedAt = dispatchedAt,
        DeadLetteredAt = deadLetteredAt,
        DeadLetterReason = deadLetterReason,
        ConcurrencyToken = Guid.NewGuid()
    };
}
