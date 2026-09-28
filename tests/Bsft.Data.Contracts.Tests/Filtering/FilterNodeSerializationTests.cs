using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Contracts.Tests.Filtering;

/// <summary>
/// The filter tree as it arrives on the wire. A browser writes it and a query compiler walks it, so
/// the discriminators and the operator spellings are the contract rather than an implementation.
/// </summary>
public sealed class FilterNodeSerializationTests
{
    private static readonly JsonSerializerOptions Options = new();

    [Fact]
    public void A_comparison_round_trips_through_its_discriminator()
    {
        var json = JsonSerializer.Serialize<FilterNode>(
            new ComparisonNode { Field = "name", Op = FilterOperator.Eq, Value = Value("\"ada\"") }, Options);

        json.Should().Contain("\"comparison\"").And.Contain("\"eq\"");

        var read = JsonSerializer.Deserialize<FilterNode>(json, Options);

        read.Should().BeOfType<ComparisonNode>();
        var comparison = (ComparisonNode)read!;
        comparison.Field.Should().Be("name");
        comparison.Op.Should().Be(FilterOperator.Eq);
        comparison.Value.GetString().Should().Be("ada");
    }

    [Fact]
    public void A_presence_test_carries_no_value_and_reads_back_as_one()
    {
        var json = JsonSerializer.Serialize<FilterNode>(new PresenceNode { Field = "email" }, Options);

        json.Should().Contain("\"present\"").And.NotContain("value");

        var read = JsonSerializer.Deserialize<FilterNode>(json, Options);

        read.Should().BeOfType<PresenceNode>();
        ((PresenceNode)read!).Field.Should().Be("email");
    }

    [Fact]
    public void A_conjunction_and_a_disjunction_keep_their_children()
    {
        FilterNode tree = new AndNode
        {
            And =
            [
                new PresenceNode { Field = "email" },
                new OrNode { Or = [new PresenceNode { Field = "phone" }] }
            ]
        };

        var read = JsonSerializer.Deserialize<FilterNode>(JsonSerializer.Serialize(tree, Options), Options);

        var and = read.Should().BeOfType<AndNode>().Subject;
        and.And.Should().HaveCount(2);
        and.And[0].Should().BeOfType<PresenceNode>();
        and.And[1].Should().BeOfType<OrNode>()
            .Which.Or.Should().ContainSingle()
            .Which.Should().BeOfType<PresenceNode>();
    }

    /// <summary>Reads one JSON value, which is the only way to get a <see cref="JsonElement"/>.</summary>
    /// <param name="json">The value's JSON.</param>
    /// <returns>The element.</returns>
    private static JsonElement Value(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
