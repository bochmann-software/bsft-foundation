using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Http.Egress.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register the outbound egress guard: the
/// address policy, the resolver behind it, the connect-time guard and the request-time handler.
/// </summary>
public static class HttpEgressServiceCollectionExtensions
{
    /// <summary>
    /// Registers the egress policy and the two guards, with the strict defaults.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Adjusts the overrides and the allow list.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Registering does not guard anything by itself: a client is guarded by
    /// <c>AddBsftHttpEgressGuard</c> on its builder. This
    /// call is what makes the policy one decision the whole application shares, so a host that widens
    /// it widens it once.
    /// </remarks>
    public static IServiceCollection AddBsftHttpEgress(
        this IServiceCollection services, Action<BsftHttpEgressOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return AddCore(services, configure);
    }

    /// <summary>
    /// Registers the same things with the options bound from configuration section
    /// <see cref="BsftHttpEgressOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftHttpEgress(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftHttpEgressOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        return AddCore(services, options =>
        {
            configuration.GetSection(BsftHttpEgressOptions.SectionName).Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers everything both entry-point overloads register.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">What shapes the options, whatever it was built from.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    private static IServiceCollection AddCore(
        IServiceCollection services, Action<BsftHttpEgressOptions>? configure)
    {
        services.AddOptions<BsftHttpEgressOptions>()
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftHttpEgressOptions>, BsftHttpEgressOptionsValidator>());

        services.TryAddSingleton<IEgressAddressResolver, SystemEgressAddressResolver>();
        services.TryAddSingleton<IOutboundAddressPolicy, OutboundAddressPolicy>();
        services.TryAddSingleton<IOutboundConnectGuard, OutboundConnectGuard>();

        // Transient, because a DelegatingHandler belongs to the chain the client factory builds and is
        // disposed with it.
        services.TryAddTransient<EgressGuardHandler>();

        return services;
    }
}
