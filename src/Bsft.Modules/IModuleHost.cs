namespace Bsft.Modules;

/// <summary>
/// Manages the lifecycle of loaded modules, coordinating startup and shutdown
/// in the correct dependency order.
/// </summary>
public interface IModuleHost
{
    /// <summary>
    /// Gets the loaded modules in dependency order.
    /// </summary>
    IReadOnlyList<IModule> Modules { get; }

    /// <summary>
    /// Gets the module catalog associated with this host.
    /// </summary>
    IModuleCatalog Catalog { get; }

    /// <summary>
    /// Starts all modules by calling <see cref="IModule.OnStartAsync"/> in dependency order.
    /// </summary>
    /// <param name="services">The service provider to pass to each module.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops all modules by calling <see cref="IModule.OnStopAsync"/> in reverse dependency order.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns module metadata suitable for the discovery endpoint.
    /// </summary>
    /// <returns>A list of module info objects containing name, version, and enabled status.</returns>
    IReadOnlyList<ModuleInfo> ListModuleInfo();
}
