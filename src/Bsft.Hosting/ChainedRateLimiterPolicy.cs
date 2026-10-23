using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Bsft.Hosting;

/// <summary>
/// One named policy of <see cref="BsftRateLimitingOptions.Policies"/> as ASP.NET Core's middleware asks for
/// it: a partition per combination of the keys its links compute for a request.
/// </summary>
/// <remarks>
/// The partition key joins the key of every link, so two requests share a <see cref="ChainedRateLimiter"/>
/// only when every link would count them against the same partition. The links hold the budgets; a
/// principal's concurrency limit chained with a global one is therefore one shared global budget, however
/// many principals there are. A rejection is answered by <see cref="RateLimitRejectionWriter"/>, through
/// <see cref="RateLimiterOptions.OnRejected"/>, so the policy brings no handler of its own.
/// </remarks>
internal sealed class ChainedRateLimiterPolicy : IRateLimiterPolicy<string>
{
    /// <summary>Separates the link keys in the partition key; no address, claim or prefix contains it.</summary>
    private const char KeySeparator = '\u001f';

    private readonly PolicyLimiterLink[] _links;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="ChainedRateLimiterPolicy"/> class.</summary>
    /// <param name="links">The links in the order the chain asks them: concurrency first.</param>
    /// <param name="timeProvider">The clock the partitions measure their idle time on.</param>
    internal ChainedRateLimiterPolicy(PolicyLimiterLink[] links, TimeProvider timeProvider)
    {
        _links = links;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

    /// <inheritdoc />
    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var keys = new string[_links.Length];
        for (var index = 0; index < _links.Length; index++)
            keys[index] = _links[index].KeyFor(httpContext);

        return RateLimitPartition.Get(
            string.Join(KeySeparator, keys),
            _ => new ChainedRateLimiter(_links, keys, _timeProvider));
    }
}
