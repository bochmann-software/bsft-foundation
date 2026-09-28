using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Tests.Support;
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
}
