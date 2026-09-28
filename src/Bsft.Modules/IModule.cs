using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules;

/// <summary>
/// Defines a pluggable module that can register services, start, and stop as part of the application lifecycle.
/// Localization keys for display name and description are resolved by convention:
/// <c>modules.{Name}.displayName</c> and <c>modules.{Name}.description</c>.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Gets the unique technical name of this module.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the semantic version of this module.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the names of modules that this module depends on.
    /// Modules listed here will be loaded and started before this module.
    /// </summary>
    IReadOnlyList<string> DependsOn => [];

    /// <summary>
    /// Registers services required by this module into the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    /// <param name="configuration">The application configuration.</param>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Called after all modules have registered their services. Performs module startup logic.
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task OnStartAsync(IServiceProvider services, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called during application shutdown. Performs module cleanup logic.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task OnStopAsync(CancellationToken cancellationToken = default);
}
