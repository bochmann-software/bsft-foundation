using System.Text.Json;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

public sealed class FilterInValuesTests
{
    private static JsonElement JsonValue(object? value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement JsonArray(params object?[] values) => JsonSerializer.SerializeToElement(values);

    [Fact]
    public void Read_WithValidArray_ReturnsElements()
    {
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray(1, 2, 3) };

        var elements = FilterInValues.Read(node);

        elements.Should().HaveCount(3);
        elements[0].GetInt32().Should().Be(1);
        elements[1].GetInt32().Should().Be(2);
        elements[2].GetInt32().Should().Be(3);
    }

    [Fact]
    public void Read_WithValidArray_PreservesElementOrder()
    {
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray("z", "a", "m") };

        var elements = FilterInValues.Read(node);

        elements.Should().HaveCount(3);
        elements[0].GetString().Should().Be("z");
        elements[1].GetString().Should().Be("a");
        elements[2].GetString().Should().Be("m");
    }

    [Fact]
    public void Read_WithNullNode_ThrowsArgumentNullException()
    {
        Action act = () => FilterInValues.Read(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("node");
    }

    [Fact]
    public void Read_WithNullValue_ThrowsValidationException()
    {
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonValue(null as string) };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.Read(node));

        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"].Should().Contain(v => v.Contains("JSON array"));
    }

    [Fact]
    public void Read_WithStringValue_ThrowsValidationException()
    {
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonValue("notanarray") };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.Read(node));

        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"].Should().Contain(v => v.Contains("JSON array"));
    }

    [Fact]
    public void Read_WithEmptyArray_ThrowsValidationExceptionWithEmptyErrorCode()
    {
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray() };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.Read(node));

        ex.ErrorCode.Should().Be(FilterInValues.EmptyErrorCode);
        ex.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public void Read_WithNullElement_ThrowsValidationExceptionWithNullElementErrorCode()
    {
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray(1, null, 3) };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.Read(node));

        ex.ErrorCode.Should().Be(FilterInValues.NullElementErrorCode);
        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("contains null");
    }

    [Fact]
    public void Read_WithNullElementMessage_MentionsEqNullAlternative()
    {
        var node = new ComparisonNode { Field = "MyField", Op = FilterOperator.In, Value = JsonArray(1, null, 3) };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.Read(node));

        var message = ex.Errors["filter"][0];
        message.Should().Contain("\"op\": \"eq\"");
        message.Should().Contain("\"value\": null");
    }

    // EnsureWithinLimits tests

    [Fact]
    public void EnsureWithinLimits_WithNullRoot_ThrowsArgumentNullException()
    {
        var act = () => FilterInValues.EnsureWithinLimits(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("root");
    }

    [Fact]
    public void EnsureWithinLimits_WithDefaults_AcceptsExactlyMaxValuesPerSet()
    {
        // Default is 100 per set
        var elements = Enumerable.Range(1, 100).Cast<object>().ToArray();
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray(elements) };
        var root = new OrNode { Or = [node] };

        // Should not throw
        FilterInValues.EnsureWithinLimits(root);
    }

    [Fact]
    public void EnsureWithinLimits_WithDefaults_RefusesOneOverMaxPerSet()
    {
        // Default is 100 per set
        var elements = Enumerable.Range(1, 101).Cast<object>().ToArray();
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray(elements) };
        var root = new OrNode { Or = [node] };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.EnsureWithinLimits(root));

        ex.ErrorCode.Should().Be(FilterInValues.TooManyErrorCode);
        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("100");
    }

    [Fact]
    public void EnsureWithinLimits_WithDefaults_AcceptsMaxValuesPerFilter()
    {
        // Default is 500 total
        var fiveSetsOfHundred = Enumerable.Range(1, 5)
            .Select(i => (FilterNode)new ComparisonNode
            {
                Field = $"Field{i}",
                Op = FilterOperator.In,
                Value = JsonArray(Enumerable.Range(i * 100, 100).Cast<object>().ToArray())
            })
            .ToList();

        var root = new AndNode { And = fiveSetsOfHundred };

        // 5 * 100 = 500, should not throw
        FilterInValues.EnsureWithinLimits(root);
    }

    [Fact]
    public void EnsureWithinLimits_WithDefaults_RefusesOneMoreThanMaxPerFilter()
    {
        // Default is 500 total; create six sets of 100 = 600 total
        var sixSetsOfHundred = Enumerable.Range(1, 6)
            .Select(i => (FilterNode)new ComparisonNode
            {
                Field = $"Field{i}",
                Op = FilterOperator.In,
                Value = JsonArray(Enumerable.Range(i * 100, 100).Cast<object>().ToArray())
            })
            .ToList();

        var root = new AndNode { And = sixSetsOfHundred };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.EnsureWithinLimits(root));

        ex.ErrorCode.Should().Be(FilterInValues.TooManyErrorCode);
        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("500");
    }

    [Fact]
    public void EnsureWithinLimits_WithCustomLimits_AcceptsExactlyCustomPerSet()
    {
        var elements = Enumerable.Range(1, 3).Cast<object>().ToArray();
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray(elements) };
        var root = new OrNode { Or = [node] };

        // Should not throw with limit 3
        FilterInValues.EnsureWithinLimits(root, 3, 100);
    }

    [Fact]
    public void EnsureWithinLimits_WithCustomLimits_RefusesExcessPerSet()
    {
        var elements = Enumerable.Range(1, 4).Cast<object>().ToArray();
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray(elements) };
        var root = new OrNode { Or = [node] };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.EnsureWithinLimits(root, 3, 100));

        ex.ErrorCode.Should().Be(FilterInValues.TooManyErrorCode);
        ex.Errors["filter"][0].Should().Contain("3");
    }

    [Fact]
    public void EnsureWithinLimits_WithCustomLimits_AcceptsExactlyCustomPerFilter()
    {
        // Two sets of 3 = 6 total, with limit 6
        var twoSetsOfThree = Enumerable.Range(1, 2)
            .Select(i => (FilterNode)new ComparisonNode
            {
                Field = $"Field{i}",
                Op = FilterOperator.In,
                Value = JsonArray(Enumerable.Range(i * 3, 3).Cast<object>().ToArray())
            })
            .ToList();

        var root = new OrNode { Or = twoSetsOfThree };

        // Should not throw with filter limit 6
        FilterInValues.EnsureWithinLimits(root, 100, 6);
    }

    [Fact]
    public void EnsureWithinLimits_WithCustomLimits_RefusesExcessPerFilter()
    {
        // Two sets of 3 = 6 total, exceeds limit 5
        var twoSetsOfThree = Enumerable.Range(1, 2)
            .Select(i => (FilterNode)new ComparisonNode
            {
                Field = $"Field{i}",
                Op = FilterOperator.In,
                Value = JsonArray(Enumerable.Range(i * 3, 3).Cast<object>().ToArray())
            })
            .ToList();

        var root = new OrNode { Or = twoSetsOfThree };

        var ex = Assert.Throws<ValidationException>(() => FilterInValues.EnsureWithinLimits(root, 100, 5));

        ex.ErrorCode.Should().Be(FilterInValues.TooManyErrorCode);
        ex.Errors["filter"][0].Should().Contain("5");
    }

    [Fact]
    public void EnsureWithinLimits_WithZeroPerSet_ThrowsArgumentOutOfRangeException()
    {
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray(1) };
        var root = new OrNode { Or = [node] };

        var act = () => FilterInValues.EnsureWithinLimits(root, 0, 100);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EnsureWithinLimits_WithZeroPerFilter_ThrowsArgumentOutOfRangeException()
    {
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonArray(1) };
        var root = new OrNode { Or = [node] };

        var act = () => FilterInValues.EnsureWithinLimits(root, 100, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EnsureWithinLimits_WithNonArrayInValue_IsIgnored()
    {
        // A comparison node with a non-array value is not an `in` set and is ignored
        var node = new ComparisonNode { Field = "TestField", Op = FilterOperator.In, Value = JsonValue("not-an-array") };
        var root = new OrNode { Or = [node] };

        // Should not throw; the invalid value will be caught by Read() later, not by EnsureWithinLimits
        FilterInValues.EnsureWithinLimits(root);
    }
}
