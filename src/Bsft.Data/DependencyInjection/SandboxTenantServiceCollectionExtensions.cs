using Bsft.Data.MultiTenancy.Sandboxes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Data.DependencyInjection;

/// <summary>Extension methods for registering the sandbox tenant seam.</summary>
public static class SandboxTenantServiceCollectionExtensions
{
    /// <summary>Registers the provisioner, and insists the host brings a registry.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Adjusts the identifier prefix, the default lifetime and the retry ceiling.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Registers no <see cref="ISandboxTenantRegistry"/> and no
    /// <see cref="ISandboxTenantCleanup"/>: both are the host's, and a startup check names the registry
    /// if it is missing. A host that registers no cleanup handler gets a teardown that removes the
    /// registry entry and says so, which is an honest answer rather than a broken one.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSandboxTenants(
        this IServiceCollection services, Action<BsftSandboxTenantOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Register(services, configuration: null, configure);
    }

    /// <summary>Registers the same services, bound from <c>Bsft:SandboxTenants</c> first.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:SandboxTenants</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IServiceCollection AddBsftSandboxTenants(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftSandboxTenantOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Register(services, configuration, configure);
    }

    private static IServiceCollection Register(
        IServiceCollection services, IConfiguration? configuration, Action<BsftSandboxTenantOptions>? configure)
    {
        services.AddOptions<BsftSandboxTenantOptions>()
            .Configure(target =>
            {
                configuration?.GetSection(BsftSandboxTenantOptions.SectionName).Bind(target);
                configure?.Invoke(target);
            })
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftSandboxTenantOptions>, BsftSandboxTenantOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ISandboxTenantProvisioner, SandboxTenantProvisioner>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Bsft.Core.IStartupValidator, SandboxTenantStartupValidator>());

        return services;
    }
}
