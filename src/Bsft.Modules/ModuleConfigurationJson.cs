using System.Text.Json.Serialization;

namespace Bsft.Modules;

/// <summary>
/// Root deserialization model for the module configuration JSON file.
/// </summary>
internal sealed record ModuleConfigurationJson
{
    /// <summary>
    /// Gets the list of module entries defined in the configuration file.
    /// </summary>
    [JsonPropertyName("modules")]
    public IReadOnlyList<ModuleEntryJson> Modules { get; init; } = [];
}
