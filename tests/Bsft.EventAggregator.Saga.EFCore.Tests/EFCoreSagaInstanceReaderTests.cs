using Bsft.Data.Contracts.Pagination;
using Bsft.EventAggregator.Saga.EFCore.Tests.Support;
using Bsft.EventAggregator.Saga.Reading;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests;

/// <summary>
/// The reader a stalled saga becomes visible through.
/// </summary>
public sealed class EFCoreSagaInstanceReaderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly TestSagaDbContext _context;
    private readonly EFCoreSagaInstanceReader _reader;

    /// <summary>Opens an in-memory database with the saga tables created.</summary>
    public EFCoreSagaInstanceReaderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(_connection).Options;
        _context = new TestSagaDbContext(options);
        _context.Database.EnsureCreated();
        _reader = new EFCoreSagaInstanceReader(_context);
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
        var act = () => new EFCoreSagaInstanceReader(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    /// <summary>A listing needs a page request.</summary>
    [Fact]
    public async Task ListAsync_WithoutARequest_Throws()
    {
        var act = () => _reader.ListAsync(null, SagaInstanceOrder.UpdatedDescending, null!);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("request");
    }

    /// <summary>The default order is the most recent transition first.</summary>
    [Fact]
    public async Task ListAsync_Always_OrdersByLastTransition()
    {
        await SeedAsync(
            Row("a", updatedAt: Now.AddMinutes(-2)),
            Row("b", updatedAt: Now.AddMinutes(-1)),
            Row("c", updatedAt: Now));

        var page = await Page();

        page.Items.Select(item => item.CorrelationKey).Should().Equal("c", "b", "a");
        page.TotalCount.Should().Be(3);
        page.HasNextPage.Should().BeFalse();
    }

    /// <summary>The other orders each read the column they name.</summary>
    /// <param name="order">The order asked for.</param>
    /// <param name="first">The instance that comes first under it.</param>
    [Theory]
    [InlineData(SagaInstanceOrder.UpdatedAscending, "old")]
    [InlineData(SagaInstanceOrder.CreatedDescending, "fresh")]
    [InlineData(SagaInstanceOrder.CreatedAscending, "old")]
    public async Task ListAsync_ByAnOrder_ReadsThatColumn(SagaInstanceOrder order, string first)
    {
        await SeedAsync(
            Row("old", createdAt: Now.AddHours(-2), updatedAt: Now.AddHours(-2)),
            Row("fresh", createdAt: Now, updatedAt: Now));

        (await Page(order: order)).Items.First().CorrelationKey.Should().Be(first);
    }

    /// <summary>The page request is honoured, and the total counts what the filter matched.</summary>
    [Fact]
    public async Task ListAsync_WithAPageRequest_AnswersThatPage()
    {
        await SeedAsync(
            Row("a", updatedAt: Now.AddMinutes(-4)),
            Row("b", updatedAt: Now.AddMinutes(-3)),
            Row("c", updatedAt: Now.AddMinutes(-2)),
            Row("d", updatedAt: Now.AddMinutes(-1)),
            Row("e", updatedAt: Now));

        var page = await Page(request: new PagedRequest(Page: 2, PageSize: 2));

        page.Items.Select(item => item.CorrelationKey).Should().Equal("c", "b");
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
        await SeedAsync(Row("a"), Row("b"));

        var result = await Page(request: new PagedRequest(Page: page, PageSize: size));

        result.Page.Should().Be(Math.Max(1, page));
        result.PageSize.Should().Be(Math.Max(1, size));
        result.Items.Should().NotBeEmpty();
    }

    /// <summary>An empty filter narrows nothing.</summary>
    [Fact]
    public async Task ListAsync_WithAnEmptyFilter_MatchesEverything()
    {
        await SeedAsync(Row("a"), Row("b", isComplete: true));

        (await Page(new SagaInstanceFilter())).TotalCount.Should().Be(2);
    }

    /// <summary>
    /// The question this listing exists for: which running instance has not moved lately.
    /// </summary>
    [Fact]
    public async Task ListAsync_ByStalled_FindsWhatHasNotMoved()
    {
        await SeedAsync(
            Row("stalled", updatedAt: Now.AddHours(-3)),
            Row("moving", updatedAt: Now),
            Row("done", updatedAt: Now.AddHours(-3), isComplete: true));

        var page = await Page(new SagaInstanceFilter(
            IsComplete: false, UpdatedBefore: new DateTimeOffset(Now.AddHours(-1))));

        page.Items.Should().ContainSingle().Which.CorrelationKey.Should().Be("stalled");
    }

    /// <summary>An exact saga type matches only that saga.</summary>
    [Fact]
    public async Task ListAsync_BySagaType_MatchesOnlyIt()
    {
        await SeedAsync(Row("a", sagaType: "Orders.Fulfil"), Row("b", sagaType: "Billing.Dun"));

        (await Page(new SagaInstanceFilter(SagaType: "Orders.Fulfil"))).Items
            .Should().ContainSingle().Which.SagaType.Should().Be("Orders.Fulfil");
    }

    /// <summary>A prefix matches a whole family of sagas.</summary>
    [Fact]
    public async Task ListAsync_BySagaTypePrefix_MatchesTheFamily()
    {
        await SeedAsync(
            Row("a", sagaType: "Orders.Fulfil"),
            Row("b", sagaType: "Orders.Cancel"),
            Row("c", sagaType: "Billing.Dun"));

        (await Page(new SagaInstanceFilter(SagaTypePrefix: "Orders."))).TotalCount.Should().Be(2);
    }

    /// <summary>A prefix carrying a LIKE metacharacter matches itself, not everything.</summary>
    [Fact]
    public async Task ListAsync_ByAPrefixWithAMetacharacter_MatchesItLiterally()
    {
        await SeedAsync(Row("a", sagaType: "Orders_Fulfil"), Row("b", sagaType: "OrdersXFulfil"));

        (await Page(new SagaInstanceFilter(SagaTypePrefix: "Orders_"))).Items
            .Should().ContainSingle().Which.SagaType.Should().Be("Orders_Fulfil");
    }

    /// <summary>The correlation key and the state type each narrow to their own instance.</summary>
    [Fact]
    public async Task ListAsync_ByKeyAndStateType_MatchThem()
    {
        await SeedAsync(
            Row("order-1", stateTypeName: "FulfilState"),
            Row("order-2", stateTypeName: "DunState"));

        (await Page(new SagaInstanceFilter(CorrelationKey: "order-1"))).Items
            .Should().ContainSingle().Which.CorrelationKey.Should().Be("order-1");
        (await Page(new SagaInstanceFilter(StateTypeName: "DunState"))).Items
            .Should().ContainSingle().Which.StateTypeName.Should().Be("DunState");
    }

    /// <summary>The start window is inclusive at both ends.</summary>
    [Fact]
    public async Task ListAsync_ByStartWindow_MatchesWhatFallsInside()
    {
        await SeedAsync(
            Row("early", createdAt: Now.AddHours(-2)),
            Row("inside", createdAt: Now),
            Row("late", createdAt: Now.AddHours(2)));

        var page = await Page(new SagaInstanceFilter(
            CreatedAfter: new DateTimeOffset(Now.AddHours(-1)),
            CreatedBefore: new DateTimeOffset(Now.AddHours(1))));

        page.Items.Should().ContainSingle().Which.CorrelationKey.Should().Be("inside");
    }

    /// <summary>So is the transition window.</summary>
    [Fact]
    public async Task ListAsync_ByTransitionWindow_MatchesWhatFallsInside()
    {
        await SeedAsync(Row("early", updatedAt: Now.AddHours(-2)), Row("inside", updatedAt: Now));

        var page = await Page(new SagaInstanceFilter(UpdatedAfter: new DateTimeOffset(Now.AddHours(-1))));

        page.Items.Should().ContainSingle().Which.CorrelationKey.Should().Be("inside");
    }

    /// <summary>The step floor finds what got past a given point.</summary>
    [Fact]
    public async Task ListAsync_ByMinimumStep_MatchesWhatGotThere()
    {
        await SeedAsync(Row("early", currentStepIndex: 1), Row("far", currentStepIndex: 4));

        (await Page(new SagaInstanceFilter(MinStepIndex: 3))).Items
            .Should().ContainSingle().Which.CurrentStepIndex.Should().Be(4);
    }

    /// <summary>A listing row carries the progress and, deliberately, no state.</summary>
    [Fact]
    public async Task ListAsync_WithARow_CarriesItsMetadata()
    {
        await SeedAsync(Row(
            "order-1",
            sagaType: "Orders.Fulfil",
            stateTypeName: "FulfilState",
            currentStepIndex: 3,
            isComplete: true,
            createdAt: Now.AddHours(-1),
            updatedAt: Now));

        var summary = (await Page()).Items.Should().ContainSingle().Subject;

        summary.SagaType.Should().Be("Orders.Fulfil");
        summary.CorrelationKey.Should().Be("order-1");
        summary.StateTypeName.Should().Be("FulfilState");
        summary.CurrentStepIndex.Should().Be(3);
        summary.IsComplete.Should().BeTrue();
        summary.CreatedAt.Should().Be(new DateTimeOffset(Now.AddHours(-1)));
        summary.UpdatedAt.Should().Be(new DateTimeOffset(Now));
        summary.Id.Should().BeGreaterThan(0);
    }

    /// <summary>The state comes back only from the read that asks for one instance.</summary>
    [Fact]
    public async Task FindAsync_WithAnInstance_AnswersItWithTheState()
    {
        await SeedAsync(Row("order-1", stateJson: """{"paid":true}"""));

        var id = _context.SagaStates.Single().Id;

        var view = await _reader.FindAsync(id);

        view!.Summary.CorrelationKey.Should().Be("order-1");
        view.StateJson.Should().Be("""{"paid":true}""");
    }

    /// <summary>An instance that is not there reads as absent.</summary>
    [Fact]
    public async Task FindAsync_WithoutAnInstance_AnswersNull() => (await _reader.FindAsync(9999)).Should().BeNull();

    /// <summary>Reads one page the way most of these tests want it.</summary>
    /// <param name="filter">What to narrow by.</param>
    /// <param name="order">How to order.</param>
    /// <param name="request">Which page.</param>
    /// <returns>The page.</returns>
    private Task<PagedResult<SagaInstanceSummary>> Page(
        SagaInstanceFilter? filter = null,
        SagaInstanceOrder order = SagaInstanceOrder.UpdatedDescending,
        PagedRequest? request = null) =>
        _reader.ListAsync(filter, order, request ?? new PagedRequest());

    /// <summary>Writes the given rows.</summary>
    /// <param name="rows">The rows to write.</param>
    /// <returns>A task that completes once they are written.</returns>
    private async Task SeedAsync(params SagaStateEntity[] rows)
    {
        _context.SagaStates.AddRange(rows);
        await _context.SaveChangesAsync();
    }

    /// <summary>Builds one saga state row.</summary>
    /// <param name="correlationKey">What identifies the instance.</param>
    /// <param name="sagaType">The saga definition.</param>
    /// <param name="stateTypeName">The state's CLR type.</param>
    /// <param name="stateJson">The serialized state.</param>
    /// <param name="currentStepIndex">How far it got.</param>
    /// <param name="isComplete">Whether it finished.</param>
    /// <param name="createdAt">When it started.</param>
    /// <param name="updatedAt">When it last moved.</param>
    /// <returns>The row.</returns>
    private static SagaStateEntity Row(
        string correlationKey,
        string sagaType = "Orders.Fulfil",
        string stateTypeName = "FulfilState",
        string stateJson = "{}",
        int currentStepIndex = 0,
        bool isComplete = false,
        DateTime? createdAt = null,
        DateTime? updatedAt = null) => new()
    {
        SagaType = sagaType,
        CorrelationKey = correlationKey,
        StateTypeName = stateTypeName,
        StateJson = stateJson,
        CurrentStepIndex = currentStepIndex,
        IsComplete = isComplete,
        CreatedAt = createdAt ?? Now,
        UpdatedAt = updatedAt ?? Now,
        ConcurrencyToken = Guid.NewGuid()
    };
}
