using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

public class FilterNodeSerializationTests
{
    private static readonly JsonSerializerOptions OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Deserialize_ComparisonNode_ReturnsCorrectType()
    {
        var json = """
        {
            "$type": "comparison",
            "field": "Name",
            "op": "eq",
            "value": "Alice"
        }
        """;

        var node = JsonSerializer.Deserialize<FilterNode>(json, OPTIONS);

        node.Should().BeOfType<ComparisonNode>();
        var comparison = (ComparisonNode)node!;
        comparison.Field.Should().Be("Name");
        comparison.Op.Should().Be(FilterOperator.Eq);
        comparison.Value.GetString().Should().Be("Alice");
    }

    [Fact]
    public void Deserialize_AndNode_ReturnsCorrectType()
    {
        var json = """
        {
            "$type": "and",
            "and": [
                { "$type": "comparison", "field": "Name", "op": "eq", "value": "Alice" },
                { "$type": "comparison", "field": "Age", "op": "gt", "value": 25 }
            ]
        }
        """;

        var node = JsonSerializer.Deserialize<FilterNode>(json, OPTIONS);

        node.Should().BeOfType<AndNode>();
        var andNode = (AndNode)node!;
        andNode.And.Should().HaveCount(2);
        andNode.And[0].Should().BeOfType<ComparisonNode>();
        andNode.And[1].Should().BeOfType<ComparisonNode>();
    }

    [Fact]
    public void Deserialize_OrNode_ReturnsCorrectType()
    {
        var json = """
        {
            "$type": "or",
            "or": [
                { "$type": "comparison", "field": "Name", "op": "contains", "value": "Ali" },
                { "$type": "comparison", "field": "Name", "op": "startsWith", "value": "Bob" }
            ]
        }
        """;

        var node = JsonSerializer.Deserialize<FilterNode>(json, OPTIONS);

        node.Should().BeOfType<OrNode>();
        var orNode = (OrNode)node!;
        orNode.Or.Should().HaveCount(2);
    }

    [Fact]
    public void Deserialize_NestedNodes_WorksCorrectly()
    {
        var json = """
        {
            "$type": "and",
            "and": [
                {
                    "$type": "or",
                    "or": [
                        { "$type": "comparison", "field": "Name", "op": "eq", "value": "Alice" },
                        { "$type": "comparison", "field": "Name", "op": "eq", "value": "Bob" }
                    ]
                },
                { "$type": "comparison", "field": "Age", "op": "gte", "value": 18 }
            ]
        }
        """;

        var node = JsonSerializer.Deserialize<FilterNode>(json, OPTIONS);

        node.Should().BeOfType<AndNode>();
        var andNode = (AndNode)node!;
        andNode.And.Should().HaveCount(2);
        andNode.And[0].Should().BeOfType<OrNode>();
        andNode.And[1].Should().BeOfType<ComparisonNode>();
    }

    [Fact]
    public void Deserialize_FilterOperator_CustomNames_Work()
    {
        var json = """
        { "$type": "comparison", "field": "Name", "op": "!contains", "value": "test" }
        """;

        var node = JsonSerializer.Deserialize<FilterNode>(json, OPTIONS);

        var comparison = node.Should().BeOfType<ComparisonNode>().Subject;
        comparison.Op.Should().Be(FilterOperator.NotContains);
    }

    [Fact]
    public void Serialize_ComparisonNode_ProducesCorrectJson()
    {
        var node = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.Contains,
            Value = JsonSerializer.SerializeToElement("test")
        };

        var json = JsonSerializer.Serialize<FilterNode>(node, OPTIONS);

        json.Should().Contain("\"$type\":\"comparison\"");
        json.Should().Contain("\"contains\"");
    }

    [Fact]
    public void Serialize_FilterOperator_UsesCustomNames()
    {
        var node = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.NotStartsWith,
            Value = JsonSerializer.SerializeToElement("x")
        };

        var json = JsonSerializer.Serialize<FilterNode>(node, OPTIONS);

        json.Should().Contain("\"!startsWith\"");
    }

    [Fact]
    public void Deserialize_PresenceNode_ReturnsCorrectType()
    {
        var json = """
        { "$type": "present", "field": "Name" }
        """;

        var node = JsonSerializer.Deserialize<FilterNode>(json, OPTIONS);

        var presence = node.Should().BeOfType<PresenceNode>().Subject;
        presence.Field.Should().Be("Name");
    }

    [Fact]
    public void Serialize_PresenceNode_ProducesPresentDiscriminator()
    {
        var node = new PresenceNode { Field = "Name" };

        var json = JsonSerializer.Serialize<FilterNode>(node, OPTIONS);

        json.Should().Contain("\"$type\":\"present\"");
        json.Should().Contain("\"Name\"");
    }

    [Fact]
    public void FilterOperator_Present_RoundTripsAsPr()
    {
        // The SCIM `pr` operator name maps to FilterOperator.Present in both directions.
        var json = """{ "$type": "comparison", "field": "Name", "op": "pr", "value": null }""";

        var node = JsonSerializer.Deserialize<FilterNode>(json, OPTIONS);
        var comparison = node.Should().BeOfType<ComparisonNode>().Subject;
        comparison.Op.Should().Be(FilterOperator.Present);

        var serialized = JsonSerializer.Serialize<FilterNode>(
            new ComparisonNode { Field = "Name", Op = FilterOperator.Present, Value = comparison.Value }, OPTIONS);
        serialized.Should().Contain("\"pr\"");
    }

    [Fact]
    public void FilterOperator_Present_IsAppendedAfterExistingOperators()
    {
        // Additive-only guarantee: Present sits past every pre-existing operator, so no existing enum
        // value shifted and no serialized filter changed meaning.
        ((int)FilterOperator.Present).Should().BeGreaterThan((int)FilterOperator.NotEndsWith);
    }

    [Fact]
    public void Deserialize_UnknownFilterOperator_ThrowsJsonException()
    {
        // Exercises FilterOperatorJsonConverter.Read line 93: unknown operator -> throws JsonException.
        var json = """
        { "$type": "comparison", "field": "Name", "op": "unknown_operator", "value": "test" }
        """;

        var act = () => JsonSerializer.Deserialize<FilterNode>(json, OPTIONS);

        act.Should().Throw<JsonException>().WithMessage("*unknown_operator*");
    }

    [Fact]
    public void Serialize_FilterOperator_UnknownEnumValue_FallsBackToToString()
    {
        // Exercises FilterOperatorJsonConverter.Write line 105:
        // a FilterOperator value not in _valueToName -> falls back to .ToString().
        // Cast an integer to FilterOperator that is not a defined enum value.
        var unknownOp = (FilterOperator)999;
        var converter = new FilterOperatorJsonConverter();

        var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WritePropertyName("op");
        converter.Write(writer, unknownOp, OPTIONS);
        writer.WriteEndObject();
        writer.Flush();

        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Contain("999"); // FilterOperator 999 stringifies to "999"
    }
}
