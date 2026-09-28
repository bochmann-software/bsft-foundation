using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Contracts.Tests.Filtering;

/// <summary>
/// How an operator is spelled on the wire. The spellings are what a client types into a query string,
/// so every one of them is pinned rather than derived from the enum's own names.
/// </summary>
public sealed class FilterOperatorJsonConverterTests
{
    [Theory]
    [InlineData(FilterOperator.Eq, "eq")]
    [InlineData(FilterOperator.Neq, "neq")]
    [InlineData(FilterOperator.Gt, "gt")]
    [InlineData(FilterOperator.Gte, "gte")]
    [InlineData(FilterOperator.Lt, "lt")]
    [InlineData(FilterOperator.Lte, "lte")]
    [InlineData(FilterOperator.Contains, "contains")]
    [InlineData(FilterOperator.NotContains, "!contains")]
    [InlineData(FilterOperator.StartsWith, "startsWith")]
    [InlineData(FilterOperator.NotStartsWith, "!startsWith")]
    [InlineData(FilterOperator.EndsWith, "endsWith")]
    [InlineData(FilterOperator.NotEndsWith, "!endsWith")]
    [InlineData(FilterOperator.Present, "pr")]
    public void Every_operator_is_written_and_read_back_under_its_wire_name(
        FilterOperator op, string wireName)
    {
        JsonSerializer.Serialize(op).Should().Be($"\"{wireName}\"");
        JsonSerializer.Deserialize<FilterOperator>($"\"{wireName}\"").Should().Be(op);
    }

    /// <summary>A hand-written query string is as likely to say EQ as eq.</summary>
    [Fact]
    public void An_operator_is_read_whatever_it_is_capitalized_as()
        => JsonSerializer.Deserialize<FilterOperator>("\"STARTSWITH\"").Should().Be(FilterOperator.StartsWith);

    [Theory]
    [InlineData("\"like\"")]
    [InlineData("null")]
    public void An_operator_nobody_defined_is_refused_rather_than_guessed(string json)
    {
        var read = () => JsonSerializer.Deserialize<FilterOperator>(json);

        read.Should().Throw<JsonException>();
    }

    /// <summary>
    /// The fallback arm of the writer. It cannot be reached through the enum's own members, so it is
    /// reached the only way a caller could: by casting a number nobody named into the enum.
    /// </summary>
    [Fact]
    public void An_operator_outside_the_table_is_written_as_its_own_name()
        => JsonSerializer.Serialize((FilterOperator)99).Should().Be("\"99\"");
}
