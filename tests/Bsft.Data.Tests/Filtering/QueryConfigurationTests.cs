using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Bsft.Data.DependencyInjection;

namespace Bsft.Data.Tests.Filtering;

public class QueryConfigurationTests
{
    private readonly QueryFieldMap<TestEntity> _fieldMap;

    public QueryConfigurationTests()
    {
        var config = new TestQueryConfiguration();
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        config.Configure(builder);
        _fieldMap = builder.BuildFieldMap();
    }

    [Fact]
    public void FieldMap_KnownField_ResolvesSuccessfully()
    {
        var mapping = _fieldMap.GetField("Name", FilterOperator.Eq);

        mapping.Should().NotBeNull();
        mapping.EntityExpression.Should().NotBeNull();
    }

    [Fact]
    public void FieldMap_UnknownField_ThrowsValidationExceptionKeyedOnFilter()
    {
        // A client-supplied unknown field is a 400, not a 500: the whitelist rejects it as a
        // ValidationException keyed on 'filter', which the exception middleware maps to a bad request.
        var act = () => _fieldMap.GetField("Unknown", FilterOperator.Eq);

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*Unknown filter field*");
    }

    [Fact]
    public void FieldMap_DisallowedOperator_ThrowsValidationExceptionKeyedOnFilter()
    {
        var act = () => _fieldMap.GetField("Name", FilterOperator.Gt);

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*not allowed*");
    }

    [Fact]
    public void FieldMap_AllowedOperator_ResolvesSuccessfully()
    {
        var mapping = _fieldMap.GetField("Age", FilterOperator.Gt);

        mapping.Should().NotBeNull();
    }

    [Fact]
    public void FieldMap_SortableField_ResolvesSort()
    {
        var expression = _fieldMap.GetSortField("Name");

        expression.Should().NotBeNull();
    }

    [Fact]
    public void FieldMap_UnknownSortField_ThrowsValidationExceptionKeyedOnSort()
    {
        var act = () => _fieldMap.GetSortField("Unknown");

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("sort")
            .WhoseValue.Should().ContainMatch("*Unknown sort field*");
    }

    [Fact]
    public void FieldMap_Contains_ReturnsTrueForKnownField()
    {
        _fieldMap.Contains("Name").Should().BeTrue();
        _fieldMap.Contains("Unknown").Should().BeFalse();
    }

    [Fact]
    public void FieldMap_CaseInsensitiveFieldLookup_Works()
    {
        _fieldMap.Contains("name").Should().BeTrue();
        _fieldMap.Contains("NAME").Should().BeTrue();
    }

    [Fact]
    public void MaxPageSize_CanBeConfigured()
    {
        var config = new TestQueryConfiguration();
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        config.Configure(builder);

        builder.GetMaxPageSize().Should().Be(50);
    }

    [Fact]
    public void AddBsftDataQueryFiltering_WithAssemblyContainingQueryConfiguration_RegistersExecutor()
    {
        // Exercises ServiceCollectionExtensions.IsQueryConfiguration (lines 54-57)
        // and GetQueryConfigurationBase (lines 59-71):
        // Scans the test assembly which contains TestQueryConfiguration : QueryConfigurationBase<TestEntity, TestDto>.
        // The IsQueryConfiguration check finds it (non-null base), GetQueryConfigurationBase walks BaseType chain.
        var services = new ServiceCollection();
        services.AddBsftDataQueryFiltering(cfg =>
            cfg.FromAssembly(typeof(TestQueryConfiguration).Assembly));

        // IQueryExecutor<TestEntity, TestDto> should be registered
        using var sp = services.BuildServiceProvider();
        var executor = sp.GetService<IQueryExecutor<TestEntity, TestDto>>();
        executor.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftDataQueryFiltering_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftDataQueryFiltering(_ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftDataQueryFiltering_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDataQueryFiltering(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void QueryFilteringConfiguration_FromAssembly_NullAssembly_ThrowsArgumentNullException()
    {
        // Exercises QueryFilteringConfiguration.FromAssembly line 20: null assembly guard.
        var config = new QueryFilteringConfiguration();

        var act = () => config.FromAssembly(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact]
    public void FieldMap_NonSortableField_ThrowsValidationExceptionKeyedOnSort()
    {
        // Exercises the non-sortable arm of GetSortField: a known-but-not-sortable field, requested
        // by a client, is a 400 keyed on 'sort'. Create a configuration with a field that has no
        // AllowSort().
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name)
            .AllowFilter(FilterOperator.Eq)
            .AllowSort();   // sortable
        builder.Field(d => d.Price)
            .AllowFilter(FilterOperator.Eq);  // NOT sortable

        var fieldMap = builder.BuildFieldMap();

        fieldMap.GetSortField("Name").Should().NotBeNull(); // sortable field works
        var act = () => fieldMap.GetSortField("Price");     // Price is not sortable

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("sort")
            .WhoseValue.Should().ContainMatch("*Sorting is not allowed*");
    }

    private static TestDbContext CreateInMemoryContext()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task QueryExecutor_ExecuteAsync_WithSortAndPagination_ReturnsPagedResult()
    {
        // Exercises QueryExecutor.ExecuteAsync lines 55-58 (ApplySort + ToPagedResultAsync).
        using var ctx = CreateInMemoryContext();
        ctx.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "Charlie", Age = 30, Email = "c@test.com", Price = 10 },
            new TestEntity { Id = 2, Name = "Alice", Age = 25, Email = "a@test.com", Price = 20 },
            new TestEntity { Id = 3, Name = "Bob", Age = 28, Email = "b@test.com", Price = 15 }
        );
        await ctx.SaveChangesAsync();

        var config = new TestQueryConfiguration();
        var executor = new QueryExecutor<TestEntity, TestDto>(config);

        var request = new PagedRequest(
            Sort: [new SortItem("Name", SortDirection.Asc)],
            Page: 1,
            PageSize: 10);

        var result = await executor.ExecuteAsync(ctx.TestEntities.AsQueryable(), request);

        result.Should().NotBeNull();
        result.Items.Should().HaveCount(3);
        result.Items[0].Name.Should().Be("Alice");
        result.Items[1].Name.Should().Be("Bob");
        result.Items[2].Name.Should().Be("Charlie");
    }

    [Fact]
    public async Task QueryExecutor_ExecuteAsync_WithFilter_FiltersEntities()
    {
        // Exercises QueryExecutor.ExecuteAsync lines 50-53: request.Filter is not null -> applies filter.
        using var ctx = CreateInMemoryContext();
        ctx.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "Alice", Age = 25, Email = "a@test.com", Price = 10 },
            new TestEntity { Id = 2, Name = "Bob", Age = 30, Email = "b@test.com", Price = 20 }
        );
        await ctx.SaveChangesAsync();

        var config = new TestQueryConfiguration();
        var executor = new QueryExecutor<TestEntity, TestDto>(config);

        // Filter: Name == "Alice"
        var filter = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.Eq,
            Value = System.Text.Json.JsonSerializer.SerializeToElement("Alice")
        };

        var request = new PagedRequest(Filter: filter, Page: 1, PageSize: 10);

        var result = await executor.ExecuteAsync(ctx.TestEntities.AsQueryable(), request);

        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Alice");
    }

    [Fact]
    public async Task QueryExecutor_ExecuteAsync_NoSortNoDefaultSort_FallsBackToTheKeyOrder()
    {
        // No sortItems and no defaultSortField: the entity's key orders the query, because paging an
        // unordered source is what DeterministicOrderingTests pins as a defect.
        using var ctx = CreateInMemoryContext();
        ctx.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "Charlie" },
            new TestEntity { Id = 2, Name = "Alice" }
        );
        await ctx.SaveChangesAsync();

        var configNoDefaultSort = new NoDefaultSortConfiguration();
        var executor = new QueryExecutor<TestEntity, TestDto>(configNoDefaultSort);

        var request = new PagedRequest(Page: 1, PageSize: 10);

        var result = await executor.ExecuteAsync(ctx.TestEntities.AsQueryable(), request);

        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
        result.Items.Select(e => e.Id).Should().Equal(1, 2);
    }

    [Fact]
    public void QueryExecutor_NullConfiguration_ThrowsArgumentNullException()
    {
        // Exercises QueryExecutor.ctor line 26-27: null configuration guard.
        var act = () => new QueryExecutor<TestEntity, TestDto>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public async Task QueryExecutor_ExecuteAsync_NullQuery_ThrowsArgumentNullException()
    {
        var config = new TestQueryConfiguration();
        var executor = new QueryExecutor<TestEntity, TestDto>(config);

        var act = async () => await executor.ExecuteAsync(null!, new PagedRequest());

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("query");
    }

    [Fact]
    public async Task QueryExecutor_ExecuteAsync_NullRequest_ThrowsArgumentNullException()
    {
        var config = new TestQueryConfiguration();
        var executor = new QueryExecutor<TestEntity, TestDto>(config);

        var act = async () => await executor.ExecuteAsync(new List<TestEntity>().AsQueryable(), null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }
}
