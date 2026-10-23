using Bsft.Instrumentation.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT rate limiting services.
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Adds rate limiting: the default fixed window per client IP address and the named policies of
    /// <see cref="BsftRateLimitingOptions.Policies"/>, with a 429 ProblemDetails answer carrying <c>Retry-After</c>.
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
    /// Adds rate limiting as <see cref="AddBsftRateLimiting(IServiceCollection, Action{BsftRateLimitingOptions}?)"/>
    /// does, bound from configuration, named policies included (<c>Bsft:Hosting:RateLimiting:Policies</c>).
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
    /// <remarks>
    /// The policies are built from <see cref="BsftRateLimitingOptions"/> when ASP.NET Core first reads its
    /// <see cref="RateLimiterOptions"/>, so they see every <c>Configure</c> a host registered and the
    /// validation runs before a policy is built. Calling this twice registers the policies once.
    /// </remarks>
    internal static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftRateLimitingOptions>? configure)
    {
        void Apply(BsftRateLimitingOptions target)
        {
            configuration?.GetSection(BsftRateLimitingOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        services.AddOptions<BsftRateLimitingOptions>().Configure(Apply).ValidateDataAnnotations().ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftRateLimitingOptions>, BsftRateLimitingOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddBsftInstrumentationSource(ConfiguredRateLimitPolicies.InstrumentationSourceName);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ConfiguredRateLimitPolicies)))
        {
            services.AddSingleton<ConfiguredRateLimitPolicies>();
            services.AddRateLimiter(_ => { });
            services.AddOptions<RateLimiterOptions>()
                .Configure<ConfiguredRateLimitPolicies>((limiterOptions, policies) => policies.ApplyTo(limiterOptions));
        }

        // Disposes the middleware's limiters with the container; UseBsftRateLimiting hands them over.
        services.TryAddSingleton<RateLimiterRelease>();

        return services;
    }
}
