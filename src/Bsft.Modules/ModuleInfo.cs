using System.Text.Json.Serialization;

namespace Bsft.Modules;

/// <summary>
/// Lightweight data transfer object representing a module in the discovery endpoint response.
/// </summary>
/// <param name="Name">The unique technical name of the module.</param>
/// <param name="Version">The semantic version of the module.</param>
/// <param name="Enabled">Whether the module is currently enabled.</param>
public sealed record ModuleInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("enabled")] bool Enabled);
