using System.Text.Json;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

/// <summary>The stored shape of what a job reports about its run.</summary>
public sealed class JobRunResultTests
{
    private static readonly JsonElement _counters = JsonSerializer.Deserialize<JsonElement>("""{"sent":118,"failed":2}""");

    [Fact]
    public void Constructor_WithSchemaAndValue_CarriesBoth()
    {
        var result = new JobRunResult("mail-dispatch/v1", _counters);

        result.Schema.Should().Be("mail-dispatch/v1");
        result.Value.GetProperty("sent").GetInt32().Should().Be(118);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithBlankSchema_Throws(string schema)
    {
        var act = () => new JobRunResult(schema, _counters);

        act.Should().Throw<ArgumentException>().WithParameterName("schema");
    }

    [Fact]
    public void Constructor_WithNullSchema_Throws()
    {
        var act = () => new JobRunResult(null!, _counters);

        act.Should().Throw<ArgumentNullException>().WithParameterName("schema");
    }

    [Fact]
    public void Constructor_WithSchemaOfMaximumLength_Accepts()
    {
        var schema = new string('s', JobRunResult.MaxSchemaLength);

        new JobRunResult(schema, _counters).Schema.Should().Be(schema);
    }

    [Fact]
    public void Constructor_WithSchemaBeyondMaximumLength_Throws()
    {
        var act = () => new JobRunResult(new string('s', JobRunResult.MaxSchemaLength + 1), _counters);

        act.Should().Throw<ArgumentException>().WithParameterName("schema");
    }

    [Fact]
    public void Constructor_WithUndefinedValue_Throws()
    {
        var act = () => new JobRunResult("mail-dispatch/v1", default);

        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void Equals_WithSameSchemaAndText_IsTrueAndHashesAlike()
    {
        var left = TestRunResults.Of("mail-dispatch/v1", """{"sent":1}""");
        var right = TestRunResults.Of("mail-dispatch/v1", """{"sent":1}""");

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Theory]
    [InlineData("mail-dispatch/v2", """{"sent":1}""")]
    [InlineData("mail-dispatch/v1", """{"sent":2}""")]
    public void Equals_WithOtherSchemaOrText_IsFalse(string schema, string json)
    {
        TestRunResults.Of("mail-dispatch/v1", """{"sent":1}""")
            .Equals(TestRunResults.Of(schema, json)).Should().BeFalse();
    }

    [Fact]
    public void Equals_WithNull_IsFalse()
    {
        TestRunResults.Of("mail-dispatch/v1", """{"sent":1}""").Equals(null).Should().BeFalse();
    }
}
