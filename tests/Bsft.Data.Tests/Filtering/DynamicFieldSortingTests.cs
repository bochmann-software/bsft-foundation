using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// Tests for sorting via dynamic fields: the sort key is resolved at request time and orders correctly.
/// </summary>
public sealed class DynamicFieldSortingTests
{
    private sealed class DynamicSortResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key) && e.Custom[key] != null);
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                {
                    FilterOperator.Eq,
                    value => e => e.Custom.ContainsKey(key) && e.Custom[key] != null &&
                        e.Custom[key]!.ToString() == value.GetString()
                }
            };
            // Sort by the string value of the custom field
            var sort = (Expression<Func<ExtensibleEntity, string>>)(e =>
                e.Custom.ContainsKey(key) && e.Custom[key] != null ? e.Custom[key]!.ToString()! : "");

            return new DynamicField<ExtensibleEntity>(present, predicates, sort);
        }
    }

    [Fact]
    public void Apply_WithDynamicFieldSort_OrdersInMemoryListCorrectly()
    {
        var entities = new List<ExtensibleEntity>
        {
            new() { Id = 1, Custom = { { "priority", "high" } } },
            new() { Id = 2, Custom = { { "priority", "low" } } },
            new() { Id = 3, Custom = { { "priority", "medium" } } },
            new() { Id = 4, Custom = { { "priority", "low" } } }
        };

        var resolver = new DynamicSortResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var sorted = QuerySorting.Apply(
            entities.AsQueryable(),
            [new SortItem("custom.priority", SortDirection.Asc)],
            map);

        var result = sorted.ToList();

        // Sorted by priority alphabetically: high, low, low, medium
        result.Select(e => e.Id).Should().Equal(1, 2, 4, 3);
    }

    [Fact]
    public void GetSortField_WithDynamicFieldSort_ReturnsTheSortKey()
    {
        var resolver = new DynamicSortResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var sortField = map.GetSortField("custom.priority");

        sortField.Should().NotBeNull();

        // Compile the lambda and apply it to sort a list
        var sortKeyCompiled = (Func<ExtensibleEntity, object>)sortField.Compile();
        var entities = new List<ExtensibleEntity>
        {
            new() { Id = 1, Custom = { { "priority", "low" } } },
            new() { Id = 2, Custom = { { "priority", "high" } } },
            new() { Id = 3, Custom = { { "priority", "medium" } } },
            new() { Id = 4 }  // Missing custom.priority, should sort to empty string
        };

        var sorted = entities.OrderBy(e => sortKeyCompiled(e)?.ToString() ?? "").ToList();

        // Sorted alphabetically: empty (missing), high, low, medium
        sorted.Select(e => e.Id).Should().Equal(4, 2, 1, 3);
    }

    [Fact]
    public void Apply_WithMultipleSortItems_AppliesDynamicSortInOrder()
    {
        var entities = new List<ExtensibleEntity>
        {
            new() { Id = 1, Name = "Alice", Custom = { { "level", "5" } } },
            new() { Id = 2, Name = "Bob", Custom = { { "level", "3" } } },
            new() { Id = 3, Name = "Alice", Custom = { { "level", "3" } } },
            new() { Id = 4, Name = "Bob", Custom = { { "level", "5" } } }
        };

        var resolver = new DynamicSortResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.Field(d => d.Name).AllowSort();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var sorted = QuerySorting.Apply(
            entities.AsQueryable(),
            [new SortItem("Name", SortDirection.Asc), new SortItem("custom.level", SortDirection.Desc)],
            map);

        var result = sorted.ToList();

        // Sorted first by Name asc (Alice, Alice, Bob, Bob), then by level desc within each name
        // Alice with 5 comes before Alice with 3, Bob with 5 comes before Bob with 3
        result.Select(e => e.Id).Should().Equal(1, 3, 4, 2);
    }
}
