using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests;

public sealed class DynamicFieldTests
{
    private static JsonElement JsonValue(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Constructor_WithValidArguments_Succeeds()
    {
        Expression<Func<ExtensibleEntity, bool>> present = e => e.Custom.ContainsKey("test");
        var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
        {
            { FilterOperator.Eq, v => e => e.Custom.ContainsKey("test") }
        };
        Expression<Func<ExtensibleEntity, int>> sort = e => 0;

        var field = new DynamicField<ExtensibleEntity>(present, predicates, sort);

        field.Present.Should().Be(present);
        field.Predicates.Should().HaveCount(1);
        field.Sort.Should().Be(sort);
    }

    [Fact]
    public void Constructor_WithNullPresent_ThrowsArgumentNullException()
    {
        var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new DynamicField<ExtensibleEntity>(null!, predicates));

        ex.ParamName.Should().Be("present");
    }

    [Fact]
    public void Constructor_WithNullPredicates_ThrowsArgumentNullException()
    {
        Expression<Func<ExtensibleEntity, bool>> present = e => true;

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new DynamicField<ExtensibleEntity>(present, null!));

        ex.ParamName.Should().Be("predicates");
    }

    [Fact]
    public void Constructor_WithPresentOperatorAsKey_ThrowsArgumentException()
    {
        Expression<Func<ExtensibleEntity, bool>> present = e => true;
        var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
        {
            { FilterOperator.Present, v => e => true }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            new DynamicField<ExtensibleEntity>(present, predicates));

        ex.ParamName.Should().Be("predicates");
        ex.Message.Should().Contain("Present");
    }

    [Fact]
    public void Constructor_WithNegatedOperatorAsKey_ThrowsArgumentException()
    {
        Expression<Func<ExtensibleEntity, bool>> present = e => true;
        var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
        {
            { FilterOperator.Neq, v => e => true }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            new DynamicField<ExtensibleEntity>(present, predicates));

        ex.ParamName.Should().Be("predicates");
    }

    [Fact]
    public void Constructor_WithNullBuilder_ThrowsArgumentException()
    {
        Expression<Func<ExtensibleEntity, bool>> present = e => true;
        var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
        {
            { FilterOperator.Eq, null! }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            new DynamicField<ExtensibleEntity>(present, predicates));

        ex.ParamName.Should().Be("predicates");
        ex.Message.Should().Contain("null");
    }

    [Fact]
    public void Constructor_WithSortNotOverExactlyOneEntity_ThrowsArgumentException()
    {
        Expression<Func<ExtensibleEntity, bool>> present = e => true;
        var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>();
        // Sort lambda takes two parameters instead of one
        Expression<Func<ExtensibleEntity, ExtensibleEntity, int>> invalidSort = (e1, e2) => 0;

        var ex = Assert.Throws<ArgumentException>(() =>
            new DynamicField<ExtensibleEntity>(present, predicates, invalidSort));

        ex.ParamName.Should().Be("sort");
        ex.Message.Should().Contain("single");
    }

    [Fact]
    public void Constructor_WithNullSort_Succeeds()
    {
        Expression<Func<ExtensibleEntity, bool>> present = e => true;
        var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>();

        var field = new DynamicField<ExtensibleEntity>(present, predicates, null);

        field.Sort.Should().BeNull();
    }

    [Fact]
    public void Constructor_AllowsAllPositiveOperators()
    {
        Expression<Func<ExtensibleEntity, bool>> present = e => true;
        var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
        {
            { FilterOperator.Eq, v => e => true },
            { FilterOperator.Gt, v => e => true },
            { FilterOperator.Gte, v => e => true },
            { FilterOperator.Lt, v => e => true },
            { FilterOperator.Lte, v => e => true },
            { FilterOperator.Contains, v => e => true },
            { FilterOperator.StartsWith, v => e => true },
            { FilterOperator.EndsWith, v => e => true },
            { FilterOperator.In, v => e => true }
        };

        var field = new DynamicField<ExtensibleEntity>(present, predicates);

        field.Predicates.Should().HaveCount(9);
    }
}
