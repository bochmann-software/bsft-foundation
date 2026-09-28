namespace Bsft.Hosting;

/// <summary>
/// The constants a host needs to name what this package registers: the CORS policy, the rate-limiting
/// policy and the console formatter.
/// </summary>
/// <remarks>
/// These are names, not tunables. A policy name is a key in someone else's registry — ASP.NET Core's
/// CORS and rate-limiting policy collections, and the logging console formatter registry — so a host
/// that wants to apply one of those policies to an endpoint, or to select the formatter itself, has to
/// spell the same string. Keeping the three in one place is what stops the string being spelled twice.
/// </remarks>
public static class HostingDefaults
{
    /// <summary>
    /// The name of the CORS policy that <c>AddBsftCors</c> registers and <c>UseBsftCors</c> applies.
    /// </summary>
    public const string CorsPolicyName = "BsftCorsPolicy";

    /// <summary>
    /// The name of the fixed-window rate-limiting policy that <c>AddBsftRateLimiting</c> registers.
    /// </summary>
    /// <remarks>
    /// <c>UseBsftRateLimiting</c> adds the limiter middleware but does not apply this policy globally, so
    /// the policy takes effect only where an endpoint asks for it by name — through
    /// <c>[EnableRateLimiting(HostingDefaults.RateLimitingPolicyName)]</c> or
    /// <c>RequireRateLimiting(HostingDefaults.RateLimitingPolicyName)</c>.
    /// </remarks>
    public const string RateLimitingPolicyName = "BsftFixedWindow";

    /// <summary>
    /// The name under which <c>BsftConsoleFormatter</c> is registered with the console logger.
    /// </summary>
    public const string ConsoleFormatterName = "bsft";
}
