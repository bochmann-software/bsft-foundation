using System.Text.Json.Serialization;

namespace Bsft.Modules;

/// <summary>
/// Deserialization model for a single module entry in the JSON configuration file.
/// </summary>
internal sealed record ModuleEntryJson
{
    /// <summary>
    /// Gets the unique technical name of the module.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the path to the assembly containing the module implementation.
    /// </summary>
    [JsonPropertyName("assembly")]
    public string Assembly { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the module is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the names of modules this module depends on.
    /// </summary>
    [JsonPropertyName("dependsOn")]
    public string[]? DependsOn { get; init; }
}
