using System.ComponentModel.DataAnnotations;

namespace Bsft.Hosting;

/// <summary>
/// Configuration options for BSFT rate limiting.
/// </summary>
public sealed class BsftRateLimitingOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:RateLimiting";

    /// <summary>
    /// Gets or sets the maximum number of requests permitted within the time window.
    /// Defaults to 100. Must be at least one.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Gets or sets the duration of the fixed time window in seconds.
    /// Defaults to 60. Must be at least one.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; set; } = 60;
}
