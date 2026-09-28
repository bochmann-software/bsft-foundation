using Bsft.Schema.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Schema.Tests.Reading;

/// <summary>
/// Reading a schema somebody submitted: what the subset admits, and that everything else is refused
/// by name rather than ignored.
/// </summary>
public sealed class SchemaReaderTests
{
    private const string Manifest = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "title": "Mailer",
          "type": "object",
          "additionalProperties": false,
          "required": ["host"],
          "properties": {
            "host": { "type": "string", "minLength": 1, "maxLength": 253, "format": "uri" },
            "port": { "type": "integer", "minimum": 1, "maximum": 65535, "default": 587 },
            "secure": { "type": "boolean" },
            "rate": { "type": "number", "minimum": 0.5 },
            "mode": { "type": "string", "enum": ["plain", "tls"] },
            "recipients": {
              "type": "array",
              "minItems": 1,
              "maxItems": 50,
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
    public void Read_AManifestInsideTheSubset_IsUsable()
    {
        var result = TestSchema.Reader().Read(TestSchema.Json(Manifest));

        result.Violations.Should().BeEmpty();
        result.IsUsable.Should().BeTrue();

        var schema = result.Schema!;

        schema.Kind.Should().Be(SchemaValueKind.Object);
        schema.Required.Should().Equal("host");
        schema.Properties.Should().ContainKeys("host", "port", "secure", "rate", "mode", "recipients", "limits");
        schema.Properties!["host"].Format.Should().Be(SchemaFormats.Uri);
        schema.Properties["host"].MaxLength.Should().Be(253);
        schema.Properties["port"].Minimum.Should().Be(1m);
        schema.Properties["port"].Default!.Value.GetInt32().Should().Be(587);
        schema.Properties["mode"].AllowedValues.Should().HaveCount(2);
        schema.Properties["recipients"].Items!.Format.Should().Be(SchemaFormats.Email);
        schema.Properties["recipients"].MinItems.Should().Be(1);
        schema.Properties["limits"].Properties.Should().ContainKey("daily");
    }

    [Fact]
    public void Read_ASchemaThatIsNotAnObject_IsRefused()
    {
        var result = TestSchema.Reader().Read(TestSchema.Json("""[1, 2]"""));

        result.IsUsable.Should().BeFalse();
        result.Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.SchemaRootNotAnObject);
    }

    [Fact]
    public void Read_ARootThatIsNotOfTypeObject_IsRefused()
    {
        // A config document is a form. A root that is a bare string has no fields to show, and nothing
        // downstream can address a value in it.
        var result = TestSchema.Reader().Read(TestSchema.Json("""{ "type": "string" }"""));

        result.Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.SchemaRootNotAnObject);
    }

    [Theory]
    [InlineData("""{ "type": "object", "properties": {}, "allOf": [] }""", SchemaCodes.SchemaKeywordNotSupported)]
    [InlineData("""{ "type": "object", "properties": {}, "$ref": "#/x" }""", SchemaCodes.SchemaKeywordNotSupported)]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "string", "maxLenght": 4 } } }""", SchemaCodes.SchemaKeywordNotSupported)]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "integer", "minLength": 4 } } }""", SchemaCodes.SchemaKeywordNotSupported)]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "boolean", "minimum": 0 } } }""", SchemaCodes.SchemaKeywordNotSupported)]
    public void Read_AKeywordOutsideTheSubset_IsRefusedRatherThanIgnored(string json, string code)
    {
        // The typo case is the whole argument: a schema declaring maxLenght declares a bound the
        // platform then does not enforce, on a screen that says the field is validated.
        var result = TestSchema.Reader().Read(TestSchema.Json(json));

        result.IsUsable.Should().BeFalse();
        result.Violations.Should().Contain(violation => violation.Code == code);
    }

    [Theory]
    [InlineData("""{ "type": "object", "properties": { "a": {} } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "null" } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": ["string", "null"] } } }""")]
    public void Read_ATypeOutsideTheSix_IsRefused(string json)
    {
        TestSchema.Reader().Read(TestSchema.Json(json))
            .Violations.Should().Contain(violation => violation.Code == SchemaCodes.SchemaTypeNotSupported);
    }

    [Fact]
    public void Read_APropertyThatIsNotASchema_IsRefused()
    {
        // Its own code, not the root's: the submission is a schema, one property of it is not.
        var violation = TestSchema.Reader().Read(TestSchema.Json("""{ "type": "object", "properties": { "a": 4 } }"""))
            .Violations.Should().ContainSingle().Subject;

        violation.Code.Should().Be(SchemaCodes.SchemaNodeNotAnObject);
        violation.Path.Should().Be("/a");
    }

    [Theory]
    [InlineData("hostname")]
    [InlineData("ipv4")]
    [InlineData("regex")]
    public void Read_AFormatOutsideTheList_IsRefused(string format)
    {
        var json = $$"""{ "type": "object", "properties": { "a": { "type": "string", "format": "{{format}}" } } }""";

        TestSchema.Reader().Read(TestSchema.Json(json))
            .Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.SchemaFormatNotSupported);
    }

    [Fact]
    public void Read_AFormatOnSomethingOtherThanAString_IsRefused()
    {
        TestSchema.Reader()
            .Read(TestSchema.Json("""{ "type": "object", "properties": { "a": { "type": "integer", "format": "email" } } }"""))
            .Violations.Should().Contain(violation => violation.Code == SchemaCodes.SchemaFormatNotSupported);
    }

    [Fact]
    public void Read_ThePresentationalFormats_AreInsideTheSubset()
    {
        // They say how a string is edited rather than what it may contain, so refusing them would
        // refuse manifests that are otherwise fine.
        var schema = TestSchema.Read("""
            {
              "type": "object",
              "properties": {
                "notes": { "type": "string", "format": "textarea" },
                "body": { "type": "string", "format": "markdown" }
              }
            }
            """);

        schema.Properties!["notes"].Format.Should().Be(SchemaFormats.TextArea);
        schema.Properties["body"].Format.Should().Be(SchemaFormats.Markdown);
    }

    [Fact]
    public void Read_APatternThatNeedsBacktracking_IsRefused()
    {
        // Backreferences and lookaround are what backtracking is for, and backtracking is what makes a
        // submitted expression a denial of service. The narrower language is the intended trade.
        var result = TestSchema.Reader()
            .Read(TestSchema.Json("""{ "type": "object", "properties": { "a": { "type": "string", "pattern": "(?=x)y" } } }"""));

        result.IsUsable.Should().BeFalse();
        result.Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.SchemaPatternNotSupported);
    }

    [Fact]
    public void Read_APatternThatIsNotAnExpression_IsRefused()
    {
        TestSchema.Reader()
            .Read(TestSchema.Json("""{ "type": "object", "properties": { "a": { "type": "string", "pattern": "[" } } }"""))
            .Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.SchemaPatternNotSupported);
    }

    [Fact]
    public void Read_AWorkablePattern_IsCompiled()
    {
        var schema = TestSchema.Read("""
            { "type": "object", "properties": { "a": { "type": "string", "pattern": "^[A-Z]{2}-[0-9]+$" } } }
            """);

        schema.Properties!["a"].Pattern!.IsMatch("DE-42").Should().BeTrue();
        schema.Properties["a"].Pattern!.IsMatch("de-42").Should().BeFalse();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("""{ "type": "string" }""")]
    public void Read_AdditionalPropertiesThatAdmitAnything_IsRefused(string value)
    {
        var json = $$"""{ "type": "object", "properties": {}, "additionalProperties": {{value}} }""";

        TestSchema.Reader().Read(TestSchema.Json(json))
            .Violations.Should().Contain(violation => violation.Code == SchemaCodes.SchemaKeywordNotSupported);
    }

    [Fact]
    public void Read_AnObjectWithoutProperties_IsRefused()
    {
        TestSchema.Reader().Read(TestSchema.Json("""{ "type": "object" }"""))
            .Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.SchemaKeywordMalformed);
    }

    [Fact]
    public void Read_AnArrayWithoutItems_IsRefused()
    {
        TestSchema.Reader().Read(TestSchema.Json("""{ "type": "object", "properties": { "a": { "type": "array" } } }"""))
            .Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.SchemaKeywordMalformed);
    }

    [Theory]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "string", "minLength": "x" } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "string", "maxLength": -1 } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "integer", "minimum": "x" } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "integer", "maximum": true } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "array", "items": { "type": "string" }, "minItems": 1.5 } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "array", "items": { "type": "string" }, "maxItems": "x" } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "string", "enum": [] } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "string", "enum": "plain" } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "string", "format": 4 } } }""")]
    [InlineData("""{ "type": "object", "properties": { "a": { "type": "string", "pattern": 4 } } }""")]
    [InlineData("""{ "type": "object", "properties": {}, "required": "a" }""")]
    [InlineData("""{ "type": "object", "properties": {}, "required": [4] }""")]
    [InlineData("""{ "type": "object", "properties": 4 }""")]
    public void Read_AKeywordOfTheWrongShape_IsRefused(string json)
    {
        TestSchema.Reader().Read(TestSchema.Json(json))
            .Violations.Should().Contain(violation => violation.Code == SchemaCodes.SchemaKeywordMalformed);
    }

    [Fact]
    public void Read_ARequiredPropertyNobodyDeclared_IsRefused()
    {
        // Otherwise every document is refused for missing a field the form never showed.
        TestSchema.Reader()
            .Read(TestSchema.Json("""{ "type": "object", "properties": { "a": { "type": "string" } }, "required": ["b"] }"""))
            .Violations.Should().ContainSingle().Which.Code.Should().Be(SchemaCodes.SchemaRequiredUnknown);
    }

    [Fact]
    public void Read_ASchemaDeeperThanTheBound_IsRefused()
    {
        var deep = """{ "type": "object", "properties": { "a": { "type": "object", "properties": { "b": { "type": "string" } } } } }""";

        TestSchema.Reader(bounds => bounds.MaxDepth = 2).Read(TestSchema.Json(deep))
            .Violations.Should().Contain(violation => violation.Code == SchemaCodes.SchemaTooLarge);
    }

    [Fact]
    public void Read_ASchemaWithMoreNodesThanTheBound_IsRefused()
    {
        TestSchema.Reader(bounds => bounds.MaxSchemaNodes = 2).Read(TestSchema.Json(Manifest))
            .Violations.Should().Contain(violation => violation.Code == SchemaCodes.SchemaTooLarge);
    }

    [Fact]
    public void Read_AnObjectDeclaringMorePropertiesThanTheBound_IsRefused()
    {
        TestSchema.Reader(bounds => bounds.MaxProperties = 2).Read(TestSchema.Json(Manifest))
            .Violations.Should().Contain(violation => violation.Code == SchemaCodes.SchemaTooLarge);
    }

    [Fact]
    public void Read_ARefusal_NamesWhereItHappened()
    {
        // A pointer rather than a dotted path, so a name carrying a dot or a slash still addresses one
        // field.
        var result = TestSchema.Reader().Read(TestSchema.Json("""
            { "type": "object", "properties": { "a/b": { "type": "string", "format": "hostname" } } }
            """));

        result.Violations.Should().ContainSingle().Which.Path.Should().Be("/a~1b/format");
    }
}
