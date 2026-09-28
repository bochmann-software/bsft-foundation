using Bsft.Core;
using Bsft.Hosting.StartupValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT startup validation services.
/// </summary>
public static class ValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the startup validation hosted service and the configuration validator.
    /// Package-specific validators (mediator, mapping) are registered by their own packages.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBsftStartupValidation(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddTransient<IStartupValidator, ConfigurationValidator>();
        services.AddHostedService<StartupValidationHostedService>();

        return services;
    }
}
