using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// The fallback for an entity that follows no key convention: the first declared sortable field, and
/// nothing at all when the configuration declares none.
/// </summary>
public class KeylessEntityOrderingTests : IDisposable
{
    private readonly TicketContext _context;

    public KeylessEntityOrderingTests()
    {
        var options = new DbContextOptionsBuilder<TicketContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new TicketContext(options);
        _context.Tickets.AddRange(
            new Ticket { Code = "C", Label = "third" },
            new Ticket { Code = "A", Label = "first" },
            new Ticket { Code = "B", Label = "second" });
        _context.SaveChanges();
    }

    [Fact]
    public async Task QueryExecutor_WhenTheEntityHasNoKeyByConvention_OrdersByTheFirstSortableField()
    {
        var executor = new QueryExecutor<Ticket, TicketDto>(new SortableCodeConfiguration());

        var result = await executor.ExecuteAsync(_context.Tickets, new PagedRequest(Page: 1, PageSize: 10));

        result.Items.Select(t => t.Code).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task QueryExecutor_WhenNothingCanEstablishAnOrder_LeavesTheQueryUnchanged()
    {
        var executor = new QueryExecutor<Ticket, TicketDto>(new NoSortableFieldConfiguration());

        var result = await executor.ExecuteAsync(_context.Tickets, new PagedRequest(Page: 1, PageSize: 10));

        result.Items.Select(t => t.Code).Should().BeEquivalentTo(["A", "B", "C"]);
    }

    [Fact]
    public async Task QueryExecutor_WhenTheEntityHasNoKeyAndASortIsRequested_AppliesTheSortAlone()
    {
        // Nothing to append: the requested order is all there is, and inventing one would be wrong.
        var executor = new QueryExecutor<Ticket, TicketDto>(new SortableCodeConfiguration());

        var result = await executor.ExecuteAsync(
            _context.Tickets,
            new PagedRequest(Sort: [new SortItem("Code", SortDirection.Desc)], Page: 1, PageSize: 10));

        result.Items.Select(t => t.Code).Should().Equal("C", "B", "A");
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class SortableCodeConfiguration : QueryConfigurationBase<Ticket, TicketDto>
    {
        public override void Configure(QueryConfigurationBuilder<Ticket, TicketDto> builder)
        {
            builder.Field(d => d.Label).AllowFilter(FilterOperator.Eq);
            builder.Field(d => d.Code).AllowSort();
        }
    }

    private sealed class NoSortableFieldConfiguration : QueryConfigurationBase<Ticket, TicketDto>
    {
        public override void Configure(QueryConfigurationBuilder<Ticket, TicketDto> builder) =>
            builder.Field(d => d.Label).AllowFilter(FilterOperator.Eq);
    }
}
