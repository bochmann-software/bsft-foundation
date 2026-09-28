using System.ComponentModel.DataAnnotations;

namespace Bsft.Hosting;

/// <summary>
/// Configuration options for BSFT API versioning.
/// </summary>
public sealed class BsftVersioningOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:Versioning";

    /// <summary>
    /// Gets or sets the default API version. Defaults to 1.0. Must not be negative: the whole part
    /// becomes the major version and the first decimal the minor, neither of which an
    /// <c>ApiVersion</c> accepts below zero.
    /// </summary>
    [Range(0.0, double.MaxValue)]
    public double DefaultVersion { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets a value indicating whether the default version should be assumed when the client does not specify one.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool ShouldAssumeDefaultVersionWhenUnspecified { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether available API versions are reported in the response headers.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool ShouldReportApiVersions { get; set; } = true;
}
