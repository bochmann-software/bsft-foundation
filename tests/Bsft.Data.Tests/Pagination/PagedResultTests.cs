using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Pagination;

public class PagedResultTests
{
    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task ToPagedResultAsync_FirstPage_ReturnsCorrectMetadata()
    {
        await using var context = CreateContext();
        for (var i = 1; i <= 10; i++)
            context.TestEntities.Add(new TestEntity { Id = i, Name = $"Item {i}" });
        await context.SaveChangesAsync();

        var result = await context.TestEntities.AsQueryable()
            .ToPagedResultAsync(1, 3);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(3);
        result.TotalCount.Should().Be(10);
        result.Items.Should().HaveCount(3);
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task ToPagedResultAsync_PageSizeOne_ReturnsOneItemPerPage()
    {
        await using var context = CreateContext();
        for (var i = 1; i <= 3; i++)
            context.TestEntities.Add(new TestEntity { Id = i, Name = $"Item {i}" });
        await context.SaveChangesAsync();

        var result = await context.TestEntities.OrderBy(e => e.Id).ToPagedResultAsync(2, 1);

        result.Items.Select(e => e.Id).Should().Equal(2);
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task ToPagedResultAsync_OffsetOfExactlyIntMaxValue_IsStillEvaluated()
    {
        // The offset still fits Skip's int, so the page is simply empty rather than refused.
        await using var context = CreateContext();
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Item 1" });
        await context.SaveChangesAsync();

        var result = await context.TestEntities.OrderBy(e => e.Id).ToPagedResultAsync(2, int.MaxValue);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ToPagedResultAsync_LastPage_HasNextPageIsFalse()
    {
        await using var context = CreateContext();
        for (var i = 1; i <= 5; i++)
            context.TestEntities.Add(new TestEntity { Id = i, Name = $"Item {i}" });
        await context.SaveChangesAsync();

        var result = await context.TestEntities.AsQueryable()
            .ToPagedResultAsync(2, 3);

        result.HasNextPage.Should().BeFalse();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ToPagedResultAsync_ExactPage_HasNextPageIsFalse()
    {
        await using var context = CreateContext();
        for (var i = 1; i <= 6; i++)
            context.TestEntities.Add(new TestEntity { Id = i, Name = $"Item {i}" });
        await context.SaveChangesAsync();

        var result = await context.TestEntities.AsQueryable()
            .ToPagedResultAsync(2, 3);

        result.HasNextPage.Should().BeFalse();
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ToPagedResultAsync_EmptyResult_ReturnsEmptyItems()
    {
        await using var context = CreateContext();

        var result = await context.TestEntities.AsQueryable()
            .ToPagedResultAsync(1, 10);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task ToPagedResultAsync_InvalidPage_ThrowsArgumentOutOfRange()
    {
        await using var context = CreateContext();

        var act = async () => await context.TestEntities.AsQueryable()
            .ToPagedResultAsync(0, 10);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ToPagedResultAsync_OffsetOverflow_ThrowsArgumentOutOfRange()
    {
        // Regression: (page - 1) * pageSize must be computed in 64-bit so a huge page does not
        // silently overflow into a negative Skip argument.
        await using var context = CreateContext();

        var act = async () => await context.TestEntities.AsQueryable()
            .ToPagedResultAsync(int.MaxValue, int.MaxValue);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("page");
    }

    [Fact]
    public async Task ToPagedResultAsync_InvalidPageSize_ThrowsArgumentOutOfRange()
    {
        await using var context = CreateContext();

        var act = async () => await context.TestEntities.AsQueryable()
            .ToPagedResultAsync(1, 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ToPagedResultAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Exercises QueryableExtensions.ToPagedResultAsync line 28: null query guard.
        IQueryable<TestEntity> query = null!;

        var act = async () => await query.ToPagedResultAsync(1, 10);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("query");
    }
}
