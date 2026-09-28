namespace Bsft.Hosting;

/// <summary>
/// Configuration options for BSFT response compression.
/// </summary>
public sealed class BsftCompressionOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:Compression";

    /// <summary>
    /// Gets or sets a value indicating whether Brotli compression is enabled.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool EnableBrotli { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether compression is enabled for HTTPS responses.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool EnableForHttps { get; set; }
}
