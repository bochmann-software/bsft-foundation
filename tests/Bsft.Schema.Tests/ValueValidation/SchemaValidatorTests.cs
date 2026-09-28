using System.Text.Json;
using Bsft.Schema.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Schema.Tests.ValueValidation;

/// <summary>
/// Checking a tenant's document against a schema that is already known to be inside the subset: every
/// refusal, each naming a path and saying something a person can act on.
/// </summary>
public sealed class SchemaValidatorTests
{
    private const string Mailer = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["host", "mode"],
          "properties": {
            "host": { "type": "string", "minLength": 1, "maxLength": 20 },
            "port": { "type": "integer", "minimum": 1, "maximum": 65535 },
            "secure": { "type": "boolean" },
            "rate": { "type": "number", "minimum": 0.5, "maximum": 10 },
            "mode": { "type": "string", "enum": ["plain", "tls"] },
            "code": { "type": "string", "pattern": "^[A-Z]{2}$" },
            "recipients": {
              "type": "array",
              "minItems": 1,
              "maxItems": 3,
              "items": { "type": "string", "format": "email" }
            },
            "limits": {
              "type": "object",
              "properties": { "daily": { "type": "integer" } }
            }
          }
        }
        """;

    [Fact]
    public void Validate_ADocumentThatMatches_IsAccepted()
    {
        var result = Check("""
            {
              "host": "smtp.example.com",
              "port": 587,
              "secure": true,
              "rate": 2.5,
              "mode": "tls",
              "code": "DE",
              "recipients": ["ops@example.com"],
              "limits": { "daily": 100 }
            }
            """);

        result.Violations.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ADocumentWithFourWrongFields_ReportsAllFour()
    {
        // A form that reports one wrong field at a time is a form somebody submits four times.
        var violations = Check("""
            { "host": "", "port": 0, "mode": "starttls", "code": "de" }
            """).Violations;

        violations.Select(violation => violation.Path)
            .Should().BeEquivalentTo(["/host", "/port", "/mode", "/code"]);
    }

    [Theory]
    [InlineData("""{ "host": 4, "mode": "tls" }""", "/host", SchemaCodes.ValueTypeMismatch)]
    [InlineData("""{ "host": "h", "mode": "tls", "port": 1.5 }""", "/port", SchemaCodes.ValueTypeMismatch)]
    [InlineData("""{ "host": "h", "mode": "tls", "secure": "yes" }""", "/secure", SchemaCodes.ValueTypeMismatch)]
    [InlineData("""{ "host": "h", "mode": "tls", "recipients": "a@b.com" }""", "/recipients", SchemaCodes.ValueTypeMismatch)]
    [InlineData("""{ "host": "h", "mode": "tls", "limits": 4 }""", "/limits", SchemaCodes.ValueTypeMismatch)]
    [InlineData("""{ "host": "h", "mode": "tls", "rate": "fast" }""", "/rate", SchemaCodes.ValueTypeMismatch)]
    [InlineData("""{ "mode": "tls" }""", "/host", SchemaCodes.ValueRequired)]
    [InlineData("""{ "host": "h", "mode": "tls", "extra": 1 }""", "/extra", SchemaCodes.ValueUnknownProperty)]
    [InlineData("""{ "host": "h", "mode": "imap" }""", "/mode", SchemaCodes.ValueNotAllowed)]
    [InlineData("""{ "host": "much too long a host name", "mode": "tls" }""", "/host", SchemaCodes.ValueLengthOutOfRange)]
    [InlineData("""{ "host": "", "mode": "tls" }""", "/host", SchemaCodes.ValueLengthOutOfRange)]
    [InlineData("""{ "host": "h", "mode": "tls", "port": 70000 }""", "/port", SchemaCodes.ValueOutOfRange)]
    [InlineData("""{ "host": "h", "mode": "tls", "rate": 0.1 }""", "/rate", SchemaCodes.ValueOutOfRange)]
    [InlineData("""{ "host": "h", "mode": "tls", "rate": 11 }""", "/rate", SchemaCodes.ValueOutOfRange)]
    [InlineData("""{ "host": "h", "mode": "tls", "code": "abc" }""", "/code", SchemaCodes.ValuePatternMismatch)]
    [InlineData("""{ "host": "h", "mode": "tls", "recipients": [] }""", "/recipients", SchemaCodes.ValueItemCountOutOfRange)]
    [InlineData("""{ "host": "h", "mode": "tls", "recipients": ["a@b.c","d@e.f","g@h.i","j@k.l"] }""", "/recipients", SchemaCodes.ValueItemCountOutOfRange)]
    [InlineData("""{ "host": "h", "mode": "tls", "recipients": ["nobody"] }""", "/recipients/0", SchemaCodes.ValueFormatMismatch)]
    [InlineData("""{ "host": "h", "mode": "tls", "limits": { "daily": "many" } }""", "/limits/daily", SchemaCodes.ValueTypeMismatch)]
    public void Validate_ADocumentThatDoesNot_SaysWhereAndWhy(string json, string path, string code)
    {
        var violation = Check(json).Violations.Should().ContainSingle().Subject;

        violation.Path.Should().Be(path);
        violation.Code.Should().Be(code);
        violation.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_AnIntegerTooLargeForTheRuntime_IsRefusedRatherThanAccepted()
    {
        var schema = TestSchema.Read("""{ "type": "object", "properties": { "n": { "type": "number" } } }""");

        var result = TestSchema.Validator().Validate(schema, TestSchema.Json("""{ "n": 1e400 }"""));

        result.Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.ValueOutOfRange);
    }

    [Theory]
    [InlineData("email", "ops@example.com", true)]
    [InlineData("email", "ops@", false)]
    [InlineData("email", "Ops <ops@example.com>", false)]
    [InlineData("uri", "https://example.com/x", true)]
    [InlineData("uri", "/relative", false)]
    [InlineData("uri", "C:\\share\\file", false)]
    [InlineData("uri", "example.com", false)]
    [InlineData("date", "2026-09-10", true)]
    [InlineData("date", "10.09.2026", false)]
    [InlineData("date-time", "2026-09-10T12:00:00Z", true)]
    [InlineData("date-time", "yesterday", false)]
    [InlineData("uuid", "b3c1f0de-0000-4000-8000-000000000000", true)]
    [InlineData("uuid", "not-a-uuid", false)]
    [InlineData("textarea", "anything at all", true)]
    [InlineData("markdown", "# anything", true)]
    public void Validate_AFormat_ChecksWhatItSaysAndNothingMore(string format, string value, bool accepted)
    {
        var schema = TestSchema.Read(
            $$"""{ "type": "object", "properties": { "a": { "type": "string", "format": "{{format}}" } } }""");

        var document = TestSchema.Json(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["a"] = value }));

        TestSchema.Validator().Validate(schema, document).IsValid.Should().Be(accepted);
    }

    [Fact]
    public void Validate_AChoiceComparedByItsText_TreatsCaseAsDifferentAndTrailingZerosAsSame()
    {
        // The same answer a JSON reader downstream would give, which is the only comparison that keeps
        // "accepted here" and "read there" the same set.
        var schema = TestSchema.Read("""
            { "type": "object", "properties": { "n": { "type": "number", "enum": [1.0] }, "s": { "type": "string", "enum": ["A"] } } }
            """);

        TestSchema.Validator().Validate(schema, TestSchema.Json("""{ "n": 1.0, "s": "A" }""")).IsValid.Should().BeTrue();
        TestSchema.Validator().Validate(schema, TestSchema.Json("""{ "n": 1.0, "s": "a" }""")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ADefault_IsNeverFilledIn()
    {
        // Supplying one would report a document valid that nobody submitted, and the value a host
        // stores would differ from the value it was shown.
        var schema = TestSchema.Read("""
            { "type": "object", "required": ["port"], "properties": { "port": { "type": "integer", "default": 587 } } }
            """);

        TestSchema.Validator().Validate(schema, TestSchema.Json("{}"))
            .Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.ValueRequired);
    }

    [Fact]
    public void Validate_ADocumentDeeperThanTheBound_IsRefused()
    {
        var schema = TestSchema.Read("""
            {
              "type": "object",
              "properties": { "a": { "type": "object", "properties": { "b": { "type": "string" } } } }
            }
            """);

        TestSchema.Validator(bounds => bounds.MaxDepth = 2)
            .Validate(schema, TestSchema.Json("""{ "a": { "b": "x" } }"""))
            .Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.ValueTooLarge);
    }

    [Fact]
    public void Validate_AnArrayLongerThanTheHostsCeiling_IsRefusedBeforeItIsWalked()
    {
        // The schema's own maxItems is the developer's bound; this one is the host's, and it applies
        // to an array that declared none.
        var schema = TestSchema.Read("""
            { "type": "object", "properties": { "a": { "type": "array", "items": { "type": "integer" } } } }
            """);

        var document = TestSchema.Json("""{ "a": [1, 2, 3, 4, 5] }""");

        TestSchema.Validator(bounds => bounds.MaxArrayItems = 3).Validate(schema, document)
            .Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.ValueTooLarge);
    }

    [Fact]
    public void Validate_AnObjectCarryingMoreFieldsThanTheBound_IsRefused()
    {
        var schema = TestSchema.Read("""
            {
              "type": "object",
              "properties": { "a": { "type": "integer" }, "b": { "type": "integer" }, "c": { "type": "integer" } }
            }
            """);

        var result = TestSchema.Validator(bounds => bounds.MaxProperties = 2)
            .Validate(schema, TestSchema.Json("""{ "a": 1, "b": 2, "c": 3 }"""));

        result.Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.ValueTooLarge);
    }

    [Fact]
    public void Validate_ARefusalDeepInADocument_PointsAtTheOneField()
    {
        var schema = TestSchema.Read("""
            {
              "type": "object",
              "properties": {
                "hosts": {
                  "type": "array",
                  "items": { "type": "object", "properties": { "a/b": { "type": "integer" } } }
                }
              }
            }
            """);

        TestSchema.Validator().Validate(schema, TestSchema.Json("""{ "hosts": [ { "a/b": 1 }, { "a/b": "x" } ] }"""))
            .Violations.Should().ContainSingle().Which.Path.Should().Be("/hosts/1/a~1b");
    }

    [Fact]
    public void Validate_ADocumentThatIsNotAnObject_IsRefusedAtTheRoot()
    {
        var schema = TestSchema.Read("""{ "type": "object", "properties": {} }""");

        var violation = TestSchema.Validator().Validate(schema, TestSchema.Json("[]"))
            .Violations.Should().ContainSingle().Subject;

        violation.Path.Should().BeEmpty();
        violation.Code.Should().Be(SchemaCodes.ValueTypeMismatch);
        violation.Message.Should().StartWith("This document");
    }

    [Fact]
    public void Validate_WithoutASchema_Refuses()
    {
        var act = () => TestSchema.Validator().Validate(null!, TestSchema.Json("{}"));

        act.Should().Throw<ArgumentNullException>().WithParameterName("schema");
    }

    [Fact]
    public void Validate_WithAPatternThatRunsOutOfTime_RefusesRatherThanThrows()
    {
        // The timeout is there so a large value cannot hold the request thread. A caller promised a
        // list of refusals must not be handed the timeout instead, so it reads as "does not match".
        var schema = TestSchema.Read(
            """{ "type": "object", "properties": { "code": { "type": "string", "pattern": "^[A-Za-z0-9]+$" } } }""",
            bounds => bounds.PatternTimeout = TimeSpan.FromTicks(1));

        var document = JsonSerializer.Serialize(new { code = new string('a', 200_000) });

        var result = TestSchema.Validator().Validate(schema, TestSchema.Json(document));

        result.Violations.Should().ContainSingle()
            .Which.Code.Should().Be(SchemaCodes.ValuePatternMismatch);
    }

    private static SchemaValidationResult Check(string json)
        => TestSchema.Validator().Validate(TestSchema.Read(Mailer), TestSchema.Json(json));
}
