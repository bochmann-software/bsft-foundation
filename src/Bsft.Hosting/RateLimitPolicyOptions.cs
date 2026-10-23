namespace Bsft.Hosting;

/// <summary>
/// One named rate limiting policy of <see cref="BsftRateLimitingOptions.Policies"/>: a chain of limiters
/// that a request has to pass every one of.
/// </summary>
/// <remarks>
/// <para>
/// A chain lets one endpoint group be limited per caller and as a whole at once, for example a
/// concurrency limiter per principal followed by a concurrency limiter shared by every caller
/// (<see cref="RateLimitPartitionKind.Global"/>). A rejection by any limiter rejects the request, and the
/// permits the earlier limiters had granted are handed back where they can be: a concurrency permit is
/// returned, a permit of a time-based limiter is spent.
/// </para>
/// <para>
/// Concurrency limiters are therefore asked first, whatever their position in <see cref="Limiters"/>, and
/// the time-based ones after them in the order listed: a request a concurrency limiter turns away then
/// costs no time-based permit. A request that a later time-based limiter rejects has spent a permit of
/// each earlier time-based limiter (ASP.NET Core's middleware asks a second time before it rejects, so
/// two), which matters only for a caller that is being limited already.
/// </para>
/// </remarks>
public sealed class RateLimitPolicyOptions
{
    /// <summary>
    /// Gets the limiters of the chain, bound from <c>Policies:&lt;name&gt;:Limiters:&lt;index&gt;</c>. At least one.
    /// </summary>
    public IList<PolicyLimiterOptions> Limiters { get; } = new List<PolicyLimiterOptions>();
}
