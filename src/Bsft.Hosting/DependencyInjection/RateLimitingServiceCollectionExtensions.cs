using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT rate limiting services.
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Adds fixed window rate limiting per client IP address.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftRateLimitingOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftRateLimiting(
        this IServiceCollection services,
        Action<BsftRateLimitingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds fixed window rate limiting per client IP address, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:RateLimiting</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftRateLimitingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(services, configuration, configure);
    }

    /// <summary>Binds, validates and registers, whichever overload was called.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">Adjusts what the section bound.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftRateLimitingOptions>? configure)
    {
        void Apply(BsftRateLimitingOptions target)
        {
            configuration?.GetSection(BsftRateLimitingOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftRateLimitingOptions();
        Apply(options);

        services.AddOptions<BsftRateLimitingOptions>().Configure(Apply).ValidateDataAnnotations().ValidateOnStart();

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.AddPolicy(
                HostingDefaults.RateLimitingPolicyName,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }

    /// <summary>
    /// Works out which bucket a request counts against.
    /// </summary>
    /// <param name="context">The request being limited.</param>
    /// <returns>The partition key.</returns>
    /// <remarks>
    /// <para>
    /// The remote address, so that one caller exhausting its permits costs that caller and nobody
    /// else. A single limiter shared by the whole policy would be the opposite of a rate limit: the
    /// first client to spend the budget would lock every other client out of the endpoint for the
    /// rest of the window, which is a denial of service anyone can trigger.
    /// </para>
    /// <para>
    /// Behind a reverse proxy the remote address is the proxy's, and every caller lands in one
    /// bucket again. A host deployed that way configures the forwarded-headers middleware so that
    /// the address reaching here is the client's; this package does not enable it, because trusting
    /// a forwarded header without knowing which hop set it is how a caller gets to choose its own
    /// bucket.
    /// </para>
    /// </remarks>
    private static string ClientKey(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? UnattributableClientKey;

    /// <summary>
    /// The bucket for a request that carries no remote address, as one over a pipe or an in-memory
    /// test server does. They share it rather than each getting a budget of their own.
    /// </summary>
    private const string UnattributableClientKey = "unattributable";
}
