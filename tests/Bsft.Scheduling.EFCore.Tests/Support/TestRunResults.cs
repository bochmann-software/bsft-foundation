using System.Text.Json;

namespace Bsft.Scheduling.EFCore.Tests.Support;

/// <summary>Builds run results from the JSON text a test writes them as.</summary>
internal static class TestRunResults
{
    /// <summary>Builds a result from a schema name and JSON text.</summary>
    /// <param name="schema">The schema name.</param>
    /// <param name="json">The value as JSON text.</param>
    /// <returns>The result.</returns>
    public static JobRunResult Of(string schema, string json) =>
        new(schema, JsonSerializer.Deserialize<JsonElement>(json));
}
