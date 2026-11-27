using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// Tests for composing an In predicate from an Eq predicate via <see cref="DynamicFieldPredicateExtensions.ToInPredicate{TEntity}"/>.
/// </summary>
public sealed class DynamicFieldPredicateExtensionsTests
{
    private static JsonElement JsonValue(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement JsonArray(params object[] values) => JsonSerializer.SerializeToElement(values);

    [Fact]
    public void ToInPredicate_WithNullEq_ThrowsArgumentNullException()
    {
        Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>? eqBuilder = null;

        var act = () => eqBuilder!.ToInPredicate();

        act.Should().Throw<ArgumentNullException>().WithParameterName("eq");
    }

    [Fact]
    public void ToInPredicate_WithNonArrayValue_ThrowsArgumentException()
    {
        Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>> eqBuilder = value =>
            e => e.Name == value.GetString();

        var inPredicate = eqBuilder.ToInPredicate();

        var act = () => inPredicate(JsonValue("single"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToInPredicate_WithEmptyArray_ThrowsArgumentException()
    {
        Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>> eqBuilder = value =>
            e => e.Name == value.GetString();

        var inPredicate = eqBuilder.ToInPredicate();

        var act = () => inPredicate(JsonArray());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToInPredicate_WithEqReturningNull_ThrowsInvalidOperationException()
    {
        Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>> eqBuilder = _ => null!;

        var inPredicate = eqBuilder.ToInPredicate();

        var act = () => inPredicate(JsonArray("a", "b", "c"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ToInPredicate_WithThreeElements_CompilesOrOverEq()
    {
        Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>> eqBuilder = value =>
            e => e.Name == value.GetString();

        var inPredicate = eqBuilder.ToInPredicate();
        var predicate = inPredicate(JsonArray("Alice", "Bob", "Charlie")).Compile();

        var alice = new ExtensibleEntity { Name = "Alice" };
        var bob = new ExtensibleEntity { Name = "Bob" };
        var charlie = new ExtensibleEntity { Name = "Charlie" };
        var dave = new ExtensibleEntity { Name = "Dave" };

        predicate(alice).Should().BeTrue();
        predicate(bob).Should().BeTrue();
        predicate(charlie).Should().BeTrue();
        predicate(dave).Should().BeFalse();
    }

    [Fact]
    public void ToInPredicate_WithNoMatchingElements_ReturnsFalseForAllRows()
    {
        Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>> eqBuilder = value =>
            e => e.Name == value.GetString();

        var inPredicate = eqBuilder.ToInPredicate();
        var predicate = inPredicate(JsonArray("Alice", "Bob", "Charlie")).Compile();

        var entities = new[]
        {
            new ExtensibleEntity { Name = "Dave" },
            new ExtensibleEntity { Name = "Eve" },
            new ExtensibleEntity { Name = "Frank" }
        };

        var results = entities.Select(predicate).ToList();
        results.Should().AllSatisfy(r => r.Should().BeFalse());
    }

    [Fact]
    public void ToInPredicate_WithOneMatchingElement_ReturnsTrueForThatRow()
    {
        Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>> eqBuilder = value =>
            e => e.Name == value.GetString();

        var inPredicate = eqBuilder.ToInPredicate();
        var predicate = inPredicate(JsonArray("Alice")).Compile();

        var alice = new ExtensibleEntity { Name = "Alice" };
        var bob = new ExtensibleEntity { Name = "Bob" };

        predicate(alice).Should().BeTrue();
        predicate(bob).Should().BeFalse();
    }

    [Fact]
    public void ToInPredicate_ComposesSingleElementAsOrExpression()
    {
        Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>> eqBuilder = value =>
            e => e.Id == value.GetInt32();

        var inPredicate = eqBuilder.ToInPredicate();
        var predicate = inPredicate(JsonArray(42)).Compile();

        var entity42 = new ExtensibleEntity { Id = 42 };
        var entity99 = new ExtensibleEntity { Id = 99 };

        predicate(entity42).Should().BeTrue();
        predicate(entity99).Should().BeFalse();
    }
}
