using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// Pins that a paged query is never handed to Skip/Take unordered, and that an order which is not
/// unique gets the entity's key appended so a row cannot move between pages.
/// </summary>
public class DeterministicOrderingTests : IDisposable
{
    private readonly TestDbContext _context;

    public DeterministicOrderingTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new TestDbContext(options);

        // Inserted out of key order, so an ordering that actually runs is distinguishable from the
        // order the source happens to return rows in.
        _context.TestEntities.AddRange(
            new TestEntity { Id = 3, Name = "Same", Age = 40 },
            new TestEntity { Id = 1, Name = "Same", Age = 40 },
            new TestEntity { Id = 4, Name = "Same", Age = 40 },
            new TestEntity { Id = 2, Name = "Same", Age = 40 });
        _context.SaveChanges();
    }

    [Fact]
    public async Task QueryExecutor_WhenNoSortAndNoDefaultSortAreConfigured_PagesWithoutOverlapOrLoss()
    {
        var executor = new QueryExecutor<TestEntity, TestDto>(new NoDefaultSortConfiguration());

        var first = await executor.ExecuteAsync(_context.TestEntities, new PagedRequest(Page: 1, PageSize: 2));
        var second = await executor.ExecuteAsync(_context.TestEntities, new PagedRequest(Page: 2, PageSize: 2));

        first.Items.Select(e => e.Id).Should().Equal(1, 2);
        second.Items.Select(e => e.Id).Should().Equal(3, 4);
        first.Items.Select(e => e.Id).Should().NotIntersectWith(second.Items.Select(e => e.Id));
        first.Items.Concat(second.Items).Select(e => e.Id).Should().BeEquivalentTo([1, 2, 3, 4]);
    }

    [Fact]
    public async Task QueryExecutor_WhenTheRequestedSortIsNotUnique_BreaksTheTieOnTheKey()
    {
        var executor = new QueryExecutor<TestEntity, TestDto>(new TestQueryConfiguration());

        var result = await executor.ExecuteAsync(
            _context.TestEntities,
            new PagedRequest(Sort: [new SortItem("Name", SortDirection.Asc)], Page: 1, PageSize: 10));

        result.Items.Select(e => e.Id).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task QueryExecutor_WhenTheConfiguredDefaultSortIsNotUnique_BreaksTheTieOnTheKey()
    {
        // TestQueryConfiguration declares DefaultSort(Name), and every row here carries the same name.
        var executor = new QueryExecutor<TestEntity, TestDto>(new TestQueryConfiguration());

        var result = await executor.ExecuteAsync(_context.TestEntities, new PagedRequest(Page: 1, PageSize: 10));

        result.Items.Select(e => e.Id).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task QueryExecutor_WhenTheSortAlreadyUsesTheKey_LeavesItsDirectionAlone()
    {
        // Appending the key again after a descending sort on it would add a contradictory ordering.
        var executor = new QueryExecutor<TestEntity, TestDto>(new SortableKeyConfiguration());

        var result = await executor.ExecuteAsync(
            _context.TestEntities,
            new PagedRequest(Sort: [new SortItem("Id", SortDirection.Desc)], Page: 1, PageSize: 10));

        result.Items.Select(e => e.Id).Should().Equal(4, 3, 2, 1);
    }

    [Fact]
    public async Task QueryExecutor_WhenASortFieldIsAComputedExpression_StillBreaksTheTieOnTheKey()
    {
        // A mapped expression that reads no single member cannot be compared against the key, so the
        // tie-breaker is appended rather than assumed redundant.
        var executor = new QueryExecutor<TestEntity, TestDto>(new ComputedSortConfiguration());

        var result = await executor.ExecuteAsync(
            _context.TestEntities,
            new PagedRequest(Sort: [new SortItem("Age", SortDirection.Asc)], Page: 1, PageSize: 10));

        result.Items.Select(e => e.Id).Should().Equal(1, 2, 3, 4);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A configuration that exposes the key itself as a sortable field.</summary>
    private sealed class SortableKeyConfiguration : QueryConfigurationBase<TestEntity, TestDto>
    {
        public override void Configure(QueryConfigurationBuilder<TestEntity, TestDto> builder) =>
            builder.Field(d => d.Id).AllowSort();
    }

    /// <summary>A configuration whose sortable field maps to a computed expression, not a member.</summary>
    private sealed class ComputedSortConfiguration : QueryConfigurationBase<TestEntity, TestDto>
    {
        public override void Configure(QueryConfigurationBuilder<TestEntity, TestDto> builder) =>
            builder.Field(d => d.Age).MapsTo(e => e.Age + 1).AllowSort();
    }
}
