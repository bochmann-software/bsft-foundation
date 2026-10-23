using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting;

/// <summary>
/// Configuration options for BSFT rate limiting.
/// </summary>
/// <remarks>
/// <see cref="PermitLimit"/> and <see cref="WindowSeconds"/> shape the default policy,
/// <see cref="HostingDefaults.RateLimitingPolicyName"/>: a fixed window per client address. <see cref="Policies"/>
/// adds named policies next to it, each a chain of limiters a request has to pass all of.
/// </remarks>
public sealed class BsftRateLimitingOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:RateLimiting";

    /// <summary>
    /// Gets or sets the maximum number of requests the default policy permits within the time window.
    /// Defaults to 100. Must be at least one.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Gets or sets the duration of the default policy's fixed time window in seconds.
    /// Defaults to 60. Must be at least one.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the <c>Retry-After</c> a rejection carries when the limiter that rejected it cannot
    /// estimate when a permit frees up, as a concurrency limiter cannot. Seconds; defaults to 1 and must be at
    /// least one. A limiter of a named policy overrides it with <see cref="PolicyLimiterOptions.RetryAfterSeconds"/>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int RetryAfterSeconds { get; set; } = 1;

    /// <summary>
    /// Gets the named policies registered next to the default one, bound from
    /// <c>Bsft:Hosting:RateLimiting:Policies:&lt;name&gt;</c>. An endpoint asks for one by name with
    /// <c>RequireRateLimiting(name)</c> or <c>[EnableRateLimiting(name)]</c>. Names are case-sensitive, as
    /// ASP.NET Core's policy names are, and must not repeat <see cref="HostingDefaults.RateLimitingPolicyName"/>.
    /// </summary>
    public IDictionary<string, RateLimitPolicyOptions> Policies { get; } =
        new Dictionary<string, RateLimitPolicyOptions>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the partition key selectors a limiter with <see cref="RateLimitPartitionKind.Custom"/> names in
    /// <see cref="PolicyLimiterOptions.PartitionKeySelector"/>. Set in code; configuration can only refer to
    /// them by name. A selector returning <see langword="null"/> or an empty key puts the request in the
    /// partition of its client address.
    /// </summary>
    public IDictionary<string, Func<HttpContext, string?>> PartitionKeySelectors { get; } =
        new Dictionary<string, Func<HttpContext, string?>>(StringComparer.Ordinal);
}
