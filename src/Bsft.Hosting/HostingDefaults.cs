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
    /// The <c>errorCode</c> of the ProblemDetails body of a request the rate limiter rejected with 429.
    /// </summary>
    public const string RateLimitedErrorCode = "RATE_LIMITED";

    /// <summary>
    /// The counter, on the <c>Bsft.Hosting</c> instrumentation source, that counts rejected requests per
    /// rate limiting policy (tag <c>policy</c>: the policy's name, or <c>global</c> for a rejection by
    /// <c>RateLimiterOptions.GlobalLimiter</c> or by a policy without a name).
    /// </summary>
    public const string RateLimitRejectionsInstrument = "bsft.rate_limiting.rejections";

    /// <summary>
    /// The name under which <c>BsftConsoleFormatter</c> is registered with the console logger.
    /// </summary>
    public const string ConsoleFormatterName = "bsft";

    /// <summary>
    /// The name of the instrumentation source the bounded pipelines of
    /// <c>Bsft.Hosting.BackgroundWork.Pipelines</c> emit their <c>bsft.pipeline.items</c> counter on.
    /// </summary>
    /// <remarks>
    /// The pipelines moved to <c>Bsft.Hosting.BackgroundWork</c>, which owns the name now as
    /// <see cref="Bsft.Hosting.BackgroundWork.BackgroundWorkDefaults.PipelineInstrumentationSourceName"/>; this
    /// constant stays so existing code compiles, and carries the same value.
    /// </remarks>
    public const string PipelineInstrumentationSourceName = BackgroundWork.BackgroundWorkDefaults.PipelineInstrumentationSourceName;
}
