using System.Text.Json;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

public sealed class FilterInOperatorTests
{
    private static JsonElement JsonValue(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement JsonArray(params object[] values) => JsonSerializer.SerializeToElement(values);

    [Fact]
    public void Compile_InOperatorOnInt_FiltersCorrectly()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Age).AllowFilter(FilterOperator.In);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Age", Op = FilterOperator.In, Value = JsonArray(20, 30) };

        var predicate = FilterCompiler.Compile<TestEntity>(node, map).Compile();

        predicate(new TestEntity { Age = 20 }).Should().BeTrue();
        predicate(new TestEntity { Age = 30 }).Should().BeTrue();
        predicate(new TestEntity { Age = 25 }).Should().BeFalse();
        predicate(new TestEntity { Age = 40 }).Should().BeFalse();
    }

    [Fact]
    public void Compile_InOperatorOnNullableInt_WithNullMember_ReturnsFalse()
    {
        // Use EntityWithNullableInt for a truly nullable int field
        var builder = new QueryConfigurationBuilder<EntityWithNullableInt, EntityWithNullableInt>();
        builder.Field(d => d.Size).AllowFilter(FilterOperator.In);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Size", Op = FilterOperator.In, Value = JsonArray(10, 20, 30) };

        var predicate = FilterCompiler.Compile<EntityWithNullableInt>(node, map).Compile();

        // A null member is not in any set
        predicate(new EntityWithNullableInt { Id = 1, Size = null }).Should().BeFalse();
        predicate(new EntityWithNullableInt { Id = 2, Size = 20 }).Should().BeTrue();
        predicate(new EntityWithNullableInt { Id = 3, Size = 50 }).Should().BeFalse();
    }

    [Fact]
    public void Compile_InOperatorOnStringLowerInvariant_IsCaseInsensitive()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.In);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.In, Value = JsonArray("alice", "bob") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, map).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "BOB" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Charlie" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_InOperatorOnStringCollationMode_ComparesBareMember()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.In);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.In, Value = JsonArray("alice", "bob") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, map, FilterStringMatch.Collation).Compile();

        predicate(new TestEntity { Name = "alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "bob" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
        predicate(new TestEntity { Name = "BOB" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_InOperatorWithoutExplicitAllowFilter_ThrowsValidationException()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).AllowFilter(); // Empty list, allows everything except In
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.In, Value = JsonArray("alice") };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<TestEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("not allowed");
    }

    [Fact]
    public void Compile_InOperatorOnFieldWithOnlyEqAllowFilter_ThrowsValidationException()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.In, Value = JsonArray("alice") };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<TestEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public void Compile_FieldWithoutAllowFilter_StillAcceptsEq()
    {
        // Gegenprobe: an empty AllowFilter list still accepts Eq and Present
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).AllowFilter(); // Empty list
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("alice") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, map).Compile();
        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
    }

    [Fact]
    public void Compile_InOperatorWithWrongTypeElement_ThrowsValidationException()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Age).AllowFilter(FilterOperator.In);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Age", Op = FilterOperator.In, Value = JsonArray("abc") };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<TestEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public void Compile_InEquivalentToOrOfEq_OnFixedSample()
    {
        // Simple test: verify that In operator gives same results as Or of Eq operators
        var entities = new List<TestEntity>
        {
            new TestEntity { Id = 1, Age = 25 },
            new TestEntity { Id = 2, Age = 30 },
            new TestEntity { Id = 3, Age = 35 }
        };

        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Age).AllowFilter(FilterOperator.In, FilterOperator.Eq);
        var map = builder.BuildFieldMap();

        // Test with 2 sample values to keep it simple
        var inNode = new ComparisonNode { Field = "Age", Op = FilterOperator.In, Value = JsonArray(25, 35) };
        var inPredicate = FilterCompiler.Compile<TestEntity>(inNode, map).Compile();

        // Build equivalent Or of Eq nodes
        var eq1 = new ComparisonNode { Field = "Age", Op = FilterOperator.Eq, Value = JsonValue(25) };
        var eq2 = new ComparisonNode { Field = "Age", Op = FilterOperator.Eq, Value = JsonValue(35) };
        var eq1Pred = FilterCompiler.Compile<TestEntity>(eq1, map).Compile();
        var eq2Pred = FilterCompiler.Compile<TestEntity>(eq2, map).Compile();
        var orPredicate = (TestEntity e) => eq1Pred(e) || eq2Pred(e);

        // They should match the same entities
        var inMatches = entities.Where(inPredicate).Select(e => e.Id).OrderBy(x => x).ToList();
        var orMatches = entities.Where(orPredicate).Select(e => e.Id).OrderBy(x => x).ToList();

        inMatches.Should().Equal(orMatches);
    }
}
