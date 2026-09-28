namespace Bsft.Hosting;

/// <summary>
/// Configuration options for BSFT JSON serialization defaults.
/// </summary>
public sealed class BsftJsonOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:Json";

    /// <summary>
    /// Gets or sets a value indicating whether enum values are serialized as strings.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool ShouldSerializeEnumsAsString { get; set; } = true;

    /// <summary>
    /// Gets or sets the default date format string for serialization.
    /// When <c>null</c>, the default System.Text.Json date format is used.
    /// </summary>
    public string? DefaultDateFormat { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>null</c> values are omitted from the JSON output.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool ShouldOmitNullValues { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether deserialization is case-insensitive.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool UsesCaseInsensitiveDeserialization { get; set; } = true;
}
