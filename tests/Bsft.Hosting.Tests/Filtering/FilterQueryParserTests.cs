using Bsft.Data.Contracts.Filtering;
using Bsft.Hosting.Filtering;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Xunit;
using Bsft.Core;

namespace Bsft.Hosting.Tests.Filtering;

public class FilterQueryParserTests
{
    private static FilterQueryParser CreateParser(Action<BsftFilterTransportOptions>? configure = null)
    {
        var options = new BsftFilterTransportOptions();
        configure?.Invoke(options);
        options.Validate();
        return new FilterQueryParser(Options.Create(options));
    }

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        var act = () => new FilterQueryParser(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NoFilter_ReturnsNull(string? rawFilter)
    {
        var parser = CreateParser();

        parser.Parse(rawFilter).Should().BeNull();
    }

    [Fact]
    public void Parse_ValidComparison_ReturnsComparisonNode()
    {
        var parser = CreateParser();
        var json = """{ "$type": "comparison", "field": "Name", "op": "eq", "value": "Alice" }""";

        var node = parser.Parse(json);

        var comparison = node.Should().BeOfType<ComparisonNode>().Subject;
        comparison.Field.Should().Be("Name");
        comparison.Op.Should().Be(FilterOperator.Eq);
        comparison.Value.GetString().Should().Be("Alice");
    }

    [Fact]
    public void Parse_ValidNestedAndOr_ReturnsTree()
    {
        var parser = CreateParser();
        var json = """
        {
            "$type": "and",
            "and": [
                { "$type": "or", "or": [
                    { "$type": "comparison", "field": "Name", "op": "eq", "value": "Alice" },
                    { "$type": "comparison", "field": "Name", "op": "eq", "value": "Bob" }
                ]},
                { "$type": "comparison", "field": "Age", "op": "gte", "value": 18 }
            ]
        }
        """;

        var node = parser.Parse(json);

        var and = node.Should().BeOfType<AndNode>().Subject;
        and.And.Should().HaveCount(2);
        and.And[0].Should().BeOfType<OrNode>();
        and.And[1].Should().BeOfType<ComparisonNode>();
    }

    [Fact]
    public void Parse_ValidPresence_ReturnsPresenceNode()
    {
        var parser = CreateParser();
        var json = """{ "$type": "present", "field": "Name" }""";

        var node = parser.Parse(json);

        var presence = node.Should().BeOfType<PresenceNode>().Subject;
        presence.Field.Should().Be("Name");
    }

    [Fact]
    public void Parse_PresenceNodeInsideAnd_CountsAsLeafAndParses()
    {
        // A present node is a leaf in the shape-bounds walk: it must be accepted, not rejected as an
        // unrecognized node type, and it counts toward the node budget like a comparison.
        var parser = CreateParser();
        var json = """
        {
            "$type": "and",
            "and": [
                { "$type": "present", "field": "Name" },
                { "$type": "comparison", "field": "Age", "op": "gte", "value": 18 }
            ]
        }
        """;

        var node = parser.Parse(json);

        var and = node.Should().BeOfType<AndNode>().Subject;
        and.And[0].Should().BeOfType<PresenceNode>();
        and.And[1].Should().BeOfType<ComparisonNode>();
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsValidationExceptionKeyedOnFilter()
    {
        var parser = CreateParser();

        var act = () => parser.Parse("{ this is not json");

        act.Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public void Parse_UnknownDiscriminator_ThrowsValidationException()
    {
        var parser = CreateParser();
        var json = """{ "$type": "nonsense", "field": "Name", "op": "eq", "value": "x" }""";

        var act = () => parser.Parse(json);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Parse_MissingRequiredMember_ThrowsValidationException()
    {
        var parser = CreateParser();
        // A comparison node without its required 'op' and 'value' is malformed.
        var json = """{ "$type": "comparison", "field": "Name" }""";

        var act = () => parser.Parse(json);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Parse_NodeWithoutDiscriminator_ThrowsValidationException()
    {
        // A payload with no $type must never reach the compiler as a bare FilterNode (which the
        // compiler cannot turn into a predicate and would answer with a 500). Whether the deserializer
        // rejects it outright or yields a bare node, the parser answers 400 either way.
        var parser = CreateParser();

        var act = () => parser.Parse("{}");

        act.Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public void Parse_JsonNull_ThrowsValidationException()
    {
        var parser = CreateParser();

        var act = () => parser.Parse("null");

        act.Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public void Parse_TooLong_ThrowsValidationExceptionBeforeParsing()
    {
        var parser = CreateParser(o => o.MaxFilterLength = 32);
        // Longer than 32 characters but otherwise well-formed; the length guard fires first.
        var json = """{ "$type": "comparison", "field": "Name", "op": "eq", "value": "a-fairly-long-value" }""";

        var act = () => parser.Parse(json);

        act.Should().Throw<ValidationException>()
            .Which.Errors["filter"][0].Should().Contain("too long");
    }

    [Fact]
    public void Parse_TooManyNodes_ThrowsValidationException()
    {
        var parser = CreateParser(o => o.MaxNodeCount = 3);
        // An 'and' node plus three comparisons is four nodes, over the limit of three.
        var json = """
        {
            "$type": "and",
            "and": [
                { "$type": "comparison", "field": "Name", "op": "eq", "value": "a" },
                { "$type": "comparison", "field": "Name", "op": "eq", "value": "b" },
                { "$type": "comparison", "field": "Name", "op": "eq", "value": "c" }
            ]
        }
        """;

        var act = () => parser.Parse(json);

        act.Should().Throw<ValidationException>()
            .Which.Errors["filter"][0].Should().Contain("too many");
    }

    [Fact]
    public void Parse_TooDeep_ThrowsValidationException()
    {
        var parser = CreateParser(o =>
        {
            o.MaxDepth = 2;
            o.MaxJsonReaderDepth = 64;
        });
        // and -> and -> comparison is depth 3, over the limit of two.
        var json = """
        {
            "$type": "and",
            "and": [
                { "$type": "and", "and": [
                    { "$type": "comparison", "field": "Name", "op": "eq", "value": "a" }
                ]}
            ]
        }
        """;

        var act = () => parser.Parse(json);

        act.Should().Throw<ValidationException>()
            .Which.Errors["filter"][0].Should().Contain("nested too deeply");
    }

    [Fact]
    public void Parse_ExceedsJsonReaderDepth_ThrowsValidationExceptionNotStackOverflow()
    {
        // A payload nested past the reader's hard MaxDepth must be a clean 400, not a crash. Build a
        // chain of nested 'and' arrays deeper than the reader depth and confirm it is rejected as a
        // ValidationException rather than overflowing the deserializer's stack.
        var parser = CreateParser(o =>
        {
            o.MaxJsonReaderDepth = 8;
            o.MaxDepth = 8;
        });

        var json = BuildNestedAnd(50);

        var act = () => parser.Parse(json);

        act.Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainKey("filter");
    }

    private static string BuildNestedAnd(int depth)
    {
        var prefix = string.Concat(Enumerable.Repeat("""{ "$type": "and", "and": [""", depth));
        const string leaf = """{ "$type": "comparison", "field": "Name", "op": "eq", "value": "x" }""";
        var suffix = string.Concat(Enumerable.Repeat("]}", depth));
        return prefix + leaf + suffix;
    }
}
