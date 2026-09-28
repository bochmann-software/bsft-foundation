using System.Text.Json;
using Bsft.Schema.Reading;
using Bsft.Schema.ValueValidation;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bsft.Schema.Tests.Support;

/// <summary>Builds a reader and a validator over one set of bounds.</summary>
internal static class TestSchema
{
    /// <summary>Parses a JSON document written inline in a test.</summary>
    /// <param name="json">The document.</param>
    /// <returns>Its root element, owned by the returned document's lifetime.</returns>
    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Builds a reader.</summary>
    /// <param name="bounds">Adjusts the bounds, or null for the shipped ones.</param>
    /// <returns>The reader.</returns>
    public static ISchemaReader Reader(Action<BsftSchemaOptions>? bounds = null) => new SchemaReader(Options(bounds));

    /// <summary>Builds a validator.</summary>
    /// <param name="bounds">Adjusts the bounds, or null for the shipped ones.</param>
    /// <returns>The validator.</returns>
    public static ISchemaValidator Validator(Action<BsftSchemaOptions>? bounds = null) => new SchemaValidator(Options(bounds));

    /// <summary>Reads a schema that the test asserts is inside the subset.</summary>
    /// <param name="json">The schema.</param>
    /// <param name="bounds">Adjusts the bounds, or null for the shipped ones.</param>
    /// <returns>The schema.</returns>
    public static SchemaNode Read(string json, Action<BsftSchemaOptions>? bounds = null)
    {
        var result = Reader(bounds).Read(Json(json));

        return result.Schema
            ?? throw new InvalidOperationException(
                "The schema was refused: " + string.Join("; ", result.Violations.Select(violation => violation.Message)));
    }

    private static IOptionsMonitor<BsftSchemaOptions> Options(Action<BsftSchemaOptions>? bounds)
    {
        var options = new BsftSchemaOptions();

        bounds?.Invoke(options);

        var monitor = Substitute.For<IOptionsMonitor<BsftSchemaOptions>>();

        monitor.CurrentValue.Returns(options);

        return monitor;
    }
}
