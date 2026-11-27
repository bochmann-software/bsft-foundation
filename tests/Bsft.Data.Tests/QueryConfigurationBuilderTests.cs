using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Tests.Support;
using Bsft.Core;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests;

public class QueryConfigurationBuilderTests
{
    [Fact]
    public void MaxPageSize_BelowOne_ThrowsArgumentOutOfRangeException()
    {
        // Exercises QueryConfigurationBuilder.MaxPageSize line 43: max < 1 guard.
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        var act = () => builder.MaxPageSize(0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("max");
    }

    [Fact]
    public void MaxPageSize_One_IsTheSmallestAccepted()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        builder.MaxPageSize(1);

        builder.GetMaxPageSize().Should().Be(1);
    }

    [Fact]
    public void DefaultSort_NullField_ThrowsArgumentNullException()
    {
        // Exercises QueryConfigurationBuilder.DefaultSort line 56: null field guard.
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        var act = () => builder.DefaultSort(null!, SortDirection.Asc);

        act.Should().Throw<ArgumentNullException>().WithParameterName("field");
    }

    [Fact]
    public void Field_NullExpression_ThrowsArgumentNullException()
    {
        // Exercises QueryConfigurationBuilder.Field lines 26-27: null dtoProperty guard.
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        var act = () => builder.Field(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("dtoProperty");
    }

    [Fact]
    public void Field_NonMemberExpression_ThrowsArgumentException()
    {
        // Exercises QueryConfigurationBuilder.ExtractMemberName line 99:
        // expression is not a simple property access -> throws ArgumentException.
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        // A constant expression is not a member access (not a property/field)
        var act = () => builder.Field(d => (object)"constant");

        act.Should().Throw<ArgumentException>().WithMessage("*simple property access*");
    }

    [Fact]
    public void Field_WithDtoPropertyNotOnEntity_ReturnsNullDefaultExpression()
    {
        // Exercises QueryConfigurationBuilder.TryBuildDefaultEntityExpression line 106:
        // entityProperty is null (DTO has field not present on entity) -> returns null.
        // FieldBuilder.Apply then throws InvalidOperationException because _entityExpression is null.
        var builder2 = new QueryConfigurationBuilder<TestEntity, DtoWithExtraField>();
        builder2.Field(d => d.Extra); // "Extra" is not on TestEntity -> TryBuildDefaultEntityExpression returns null

        // Building the field map should fail because _entityExpression is null
        var act = () => builder2.BuildFieldMap();

        act.Should().Throw<InvalidOperationException>().WithMessage("*No entity mapping configured for DTO field 'Extra'*");
    }

    [Fact]
    public void FieldBuilder_MapsTo_SetsExplicitEntityExpression()
    {
        // Exercises FieldBuilder.MapsTo lines 35-38.
        // TestDto.Email maps to TestEntity.Email by convention,
        // but we use MapsTo to override it with a different entity expression.
        var builder = new QueryConfigurationBuilder<TestEntity, DtoWithExtraField>();
        builder.Field(d => d.Extra)
            .MapsTo(e => e.Name); // override with entity's Name property

        // BuildFieldMap should succeed because MapsTo provided the entity expression
        var fieldMap = builder.BuildFieldMap();
        fieldMap.Should().NotBeNull();
        fieldMap.Contains("Extra").Should().BeTrue();
    }

    [Fact]
    public void FieldBuilder_MapsTo_NullExpression_ThrowsArgumentNullException()
    {
        // Exercises FieldBuilder.MapsTo line 36: null entityProperty guard.
        var builder = new QueryConfigurationBuilder<TestEntity, DtoWithExtraField>();
        var fieldBuilder = builder.Field(d => d.Extra);
        var act = () => fieldBuilder.MapsTo(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("entityProperty");
    }

    [Fact]
    public void FieldBuilder_DtoFieldName_ReturnsConfiguredFieldName()
    {
        // Exercises FieldBuilder.DtoFieldName getter (line 78).
        var builder = new QueryConfigurationBuilder<TestEntity, DtoWithExtraField>();
        var fieldBuilder = builder.Field(d => d.Extra);

        // DtoFieldName is internal but accessible via InternalsVisibleTo
        fieldBuilder.DtoFieldName.Should().Be("Extra");
    }

    [Fact]
    public void FieldBuilder_Build_ReturnsSelf()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        var fieldBuilder = builder.Field(d => d.Name);

        fieldBuilder.Build().Should().BeSameAs(fieldBuilder);
    }

    [Fact]
    public void QueryConfigBuilder_Build_ReturnsSelf()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        builder.Build().Should().BeSameAs(builder);
    }

    // MaxInValues tests

    [Fact]
    public void MaxInValues_BelowOne_ThrowsArgumentOutOfRangeException()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        var act = () => builder.MaxInValues(0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("max");
    }

    [Fact]
    public void MaxInValues_One_IsTheSmallestAccepted()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        builder.MaxInValues(1);

        var fieldMap = builder.BuildFieldMap();
        fieldMap.MaxInValuesPerSet.Should().Be(1);
    }

    [Fact]
    public void MaxInValues_CustomValue_IsRespectedByCompiler()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        builder.MaxInValues(3);
        var fieldMap = builder.BuildFieldMap();

        // A set of exactly 3 should be accepted
        var nodeWithThree = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.In,
            Value = JsonSerializer.SerializeToElement(new[] { "a", "b", "c" })
        };

        var act = () => FilterCompiler.Compile<TestEntity>(nodeWithThree, fieldMap);
        act.Should().NotThrow();

        // A set of 4 should be refused
        var nodeWithFour = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.In,
            Value = JsonSerializer.SerializeToElement(new[] { "a", "b", "c", "d" })
        };

        var actFour = () => FilterCompiler.Compile<TestEntity>(nodeWithFour, fieldMap);
        actFour.Should().Throw<ValidationException>().Where(e => e.ErrorCode == FilterInValues.TooManyErrorCode && e.Errors["filter"][0].Contains("3"));
    }

    [Fact]
    public void MaxInValues_DefaultValueIsOneHundred()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        var fieldMap = builder.BuildFieldMap();
        fieldMap.MaxInValuesPerSet.Should().Be(100);
    }

    [Fact]
    public void MaxInValues_OneDayMightyManyWhenMoreThanDefault()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        builder.MaxInValues(150);  // More than default 100
        var fieldMap = builder.BuildFieldMap();

        // A set of 150 should be accepted
        var nodeWith150 = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.In,
            Value = JsonSerializer.SerializeToElement(Enumerable.Range(1, 150).Select(i => $"v{i}").ToArray())
        };

        var act = () => FilterCompiler.Compile<TestEntity>(nodeWith150, fieldMap);
        act.Should().NotThrow();
    }

    // MaxInValuesPerFilter tests

    [Fact]
    public void MaxInValuesPerFilter_BelowOne_ThrowsArgumentOutOfRangeException()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        var act = () => builder.MaxInValuesPerFilter(0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("max");
    }

    [Fact]
    public void MaxInValuesPerFilter_One_IsTheSmallestAccepted()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        builder.MaxInValuesPerFilter(1);

        var fieldMap = builder.BuildFieldMap();
        fieldMap.MaxInValuesPerFilter.Should().Be(1);
    }

    [Fact]
    public void MaxInValuesPerFilter_DefaultValueIsFiveHundred()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        var fieldMap = builder.BuildFieldMap();
        fieldMap.MaxInValuesPerFilter.Should().Be(500);
    }

    [Fact]
    public void MaxInValuesPerFilter_LimitAppliesAcrossMultipleSets()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        builder.Field(d => d.Email).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        builder.MaxInValuesPerFilter(4);
        var fieldMap = builder.BuildFieldMap();

        // Two sets of 3 each = 6 total, exceeds limit 4
        var or = new OrNode
        {
            Or = new[]
            {
                new ComparisonNode
                {
                    Field = "Name",
                    Op = FilterOperator.In,
                    Value = JsonSerializer.SerializeToElement(new[] { "a", "b", "c" })
                },
                new ComparisonNode
                {
                    Field = "Email",
                    Op = FilterOperator.In,
                    Value = JsonSerializer.SerializeToElement(new[] { "x", "y", "z" })
                }
            }
        };

        var act = () => FilterCompiler.Compile<TestEntity>(or, fieldMap);
        act.Should().Throw<ValidationException>().Where(e => e.ErrorCode == FilterInValues.TooManyErrorCode && e.Errors["filter"][0].Contains("4"));
    }

    [Fact]
    public void MaxInValuesPerFilter_AlsoAppliesToDynamicFields()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.MaxInValuesPerFilter(4);

        var resolver = new TestDynamicFieldResolver();
        builder.DynamicFields("custom.", resolver);

        var fieldMap = builder.BuildFieldMap();

        // Dynamic field with 5 elements exceeds limit 4
        var node = new ComparisonNode
        {
            Field = "custom.priority",
            Op = FilterOperator.In,
            Value = JsonSerializer.SerializeToElement(new[] { 1, 2, 3, 4, 5 })
        };

        var act = () => FilterCompiler.Compile<TestEntity>(node, fieldMap);
        act.Should().Throw<ValidationException>().Where(e => e.ErrorCode == FilterInValues.TooManyErrorCode);
    }

    // Test helper for dynamic fields
    private sealed class TestDynamicFieldResolver : IDynamicFieldResolver<TestEntity>
    {
        public DynamicField<TestEntity>? Resolve(string key)
        {
            var present = (System.Linq.Expressions.Expression<Func<TestEntity, bool>>)(e => true);
            var predicates = new Dictionary<FilterOperator, Func<System.Text.Json.JsonElement, System.Linq.Expressions.Expression<Func<TestEntity, bool>>>>
            {
                { FilterOperator.Eq, value => e => true },
                { FilterOperator.In, ((Func<System.Text.Json.JsonElement, System.Linq.Expressions.Expression<Func<TestEntity, bool>>>)(value => e => true)).ToInPredicate() }
            };
            var sort = (System.Linq.Expressions.Expression<Func<TestEntity, object>>)(e => (object)1);
            return new DynamicField<TestEntity>(present, predicates, sort);
        }
    }
}
