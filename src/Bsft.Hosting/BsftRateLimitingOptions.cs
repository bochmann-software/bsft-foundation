using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting;

/// <summary>
/// Configuration options for BSFT rate limiting.
/// </summary>
/// <remarks>
/// <see cref="PermitLimit"/> and <see cref="WindowSeconds"/> (or <see cref="Window"/>) shape the default policy,
/// <see cref="HostingDefaults.RateLimitingPolicyName"/>: a fixed window per client address. <see cref="Policies"/>
/// adds named policies next to it, each a chain of limiters a request has to pass all of.
/// </remarks>
public sealed class BsftRateLimitingOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:RateLimiting";

    /// <summary>The default of <see cref="PermitLimit"/>, and of a limiter's permit limit.</summary>
    internal const int DefaultPermitLimit = 100;

    /// <summary>The default of <see cref="WindowSeconds"/>, which tells an unchanged value from a chosen one.</summary>
    internal const int DefaultWindowSeconds = 60;

    /// <summary>The default of <see cref="CustomPartitionKeyPrefix"/>.</summary>
    internal const string DefaultCustomPartitionKeyPrefix = "custom:";

    /// <summary>
    /// Gets or sets the maximum number of requests the default policy permits within the time window.
    /// Defaults to 100. Must be at least one.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = DefaultPermitLimit;

    /// <summary>
    /// Gets or sets the duration of the default policy's fixed time window in seconds.
    /// Defaults to 60. Must be at least one.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; set; } = DefaultWindowSeconds;

    /// <summary>
    /// Gets or sets the duration of the default policy's fixed time window as a time span, bound from
    /// strings such as <c>00:00:30</c>. <see langword="null"/>, the default, uses <see cref="WindowSeconds"/>.
    /// When set it takes precedence over <see cref="WindowSeconds"/>; changing both to values that disagree
    /// is a validation failure. Must be at least one second.
    /// </summary>
    public TimeSpan? Window { get; set; }

    /// <summary>
    /// Gets or sets the <c>Retry-After</c> a rejection carries when the limiter that rejected it cannot
    /// estimate when a permit frees up, as a concurrency limiter cannot. Seconds; defaults to 1 and must be at
    /// least one. A limiter of a named policy overrides it with <see cref="PolicyLimiterOptions.RetryAfterSeconds"/>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int RetryAfterSeconds { get; set; } = 1;

    /// <summary>Gets the window of the default policy: <see cref="Window"/> when set, else <see cref="WindowSeconds"/>.</summary>
    internal TimeSpan EffectiveWindow => Window ?? TimeSpan.FromSeconds(WindowSeconds);

    /// <summary>
    /// Gets or sets whether the default policy, <see cref="HostingDefaults.RateLimitingPolicyName"/>, is
    /// registered. Defaults to <see langword="true"/>. A host whose endpoints only ask for named policies, or
    /// that limits through <see cref="GlobalLimiter"/>, sets it to <see langword="false"/> and registers no
    /// unused policy; an endpoint that still asks for the default policy then fails as ASP.NET Core does for
    /// any unknown policy name.
    /// </summary>
    public bool EnableDefaultPolicy { get; set; } = true;

    /// <summary>
    /// Gets or sets the limiter that covers every request, ASP.NET Core's <c>RateLimiterOptions.GlobalLimiter</c>,
    /// configured like one limiter of a named policy and bound from <c>Bsft:Hosting:RateLimiting:GlobalLimiter</c>.
    /// <see langword="null"/>, the default, sets no global limiter. It runs before the endpoint policies, so a
    /// request is charged against both. A host partitions it by any key through
    /// <see cref="RateLimitPartitionKind.Custom"/> and <see cref="PartitionKeySelectors"/>.
    /// </summary>
    public PolicyLimiterOptions? GlobalLimiter { get; set; }

    /// <summary>
    /// Gets or sets the prefix of the partition key of a <see cref="RateLimitPartitionKind.Custom"/> limiter,
    /// which keeps a selector's key from sharing a partition with the <c>address:</c> and <c>principal:</c>
    /// keys of the same limiter. Defaults to <c>custom:</c>. Empty uses the selector's key as it is; the
    /// selector then must not return keys that read like <c>address:...</c> or <c>principal:...</c>. Must not be null.
    /// </summary>
    public string CustomPartitionKeyPrefix { get; set; } = DefaultCustomPartitionKeyPrefix;

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
