using System.Threading.RateLimiting;
using Bsft.Instrumentation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting;

/// <summary>
/// Turns <see cref="BsftRateLimitingOptions"/> into ASP.NET Core's <see cref="RateLimiterOptions"/>: the
/// default policy, every named policy, the rejection status and the rejection handler. Owns the limiters
/// of the named policies and disposes them with the container.
/// </summary>
/// <remarks>
/// <para>
/// The default policy is what it has always been: a fixed window of <see cref="BsftRateLimitingOptions.PermitLimit"/>
/// requests per <see cref="BsftRateLimitingOptions.WindowSeconds"/>, partitioned by client address, without
/// a queue.
/// </para>
/// <para>
/// The rejection handler is set only when the host has not set one of its own before; a host that sets
/// one afterwards replaces it, as with any options. The status stays 429 either way.
/// </para>
/// </remarks>
internal sealed class ConfiguredRateLimitPolicies : IDisposable
{
    private readonly BsftRateLimitingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IInstrumentationSourceProvider _sources;
    private readonly List<PolicyLimiterLink> _links = [];
    private bool _isDisposed;

    /// <summary>Initializes a new instance of the <see cref="ConfiguredRateLimitPolicies"/> class.</summary>
    /// <param name="options">The rate limiting options, validated when they are read.</param>
    /// <param name="timeProvider">The clock the named policies measure idle partitions on.</param>
    /// <param name="sources">Supplies the instrumentation source the rejection counter lives on.</param>
    public ConfiguredRateLimitPolicies(
        IOptions<BsftRateLimitingOptions> options, TimeProvider timeProvider, IInstrumentationSourceProvider sources)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(sources);

        _options = options.Value;
        _timeProvider = timeProvider;
        _sources = sources;
    }

    /// <summary>The instrumentation source the rejection counter is created on.</summary>
    internal const string InstrumentationSourceName = "Bsft.Hosting";

    /// <summary>Registers the policies and the rejection handling on <paramref name="limiterOptions"/>.</summary>
    /// <param name="limiterOptions">ASP.NET Core's rate limiter options.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the container has disposed this instance.</exception>
    internal void ApplyTo(RateLimiterOptions limiterOptions)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var permitLimit = _options.PermitLimit;
        var window = TimeSpan.FromSeconds(_options.WindowSeconds);

        limiterOptions.AddPolicy(
            HostingDefaults.RateLimitingPolicyName,
            context => RateLimitPartition.GetFixedWindowLimiter(
                RateLimitPartitionKeys.ClientAddress(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        foreach (var (name, policy) in _options.Policies)
            limiterOptions.AddPolicy(name, BuildPolicy(policy));

        limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        if (limiterOptions.OnRejected is null)
        {
            var rejectionWriter = new RateLimitRejectionWriter(
                _sources.Get(InstrumentationSourceName).GetOrCreateCounter(HostingDefaults.RateLimitRejectionsInstrument),
                TimeSpan.FromSeconds(_options.RetryAfterSeconds));
            limiterOptions.OnRejected = rejectionWriter.WriteAsync;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        PolicyLimiterLink[] links;
        lock (_links)
        {
            _isDisposed = true;
            links = [.. _links];
            _links.Clear();
        }

        foreach (var link in links)
            link.Dispose();
    }

    /// <summary>Builds one named policy, concurrency links first.</summary>
    /// <param name="policy">The policy's options.</param>
    /// <returns>The policy.</returns>
    private ChainedRateLimiterPolicy BuildPolicy(RateLimitPolicyOptions policy)
    {
        var links = policy.Limiters
            .Select(limiter => new PolicyLimiterLink(limiter, _options))
            .OrderBy(link => link.Kind == RateLimiterKind.Concurrency ? 0 : 1)
            .ToArray();

        lock (_links)
            _links.AddRange(links);

        return new ChainedRateLimiterPolicy(links, _timeProvider);
    }
}
