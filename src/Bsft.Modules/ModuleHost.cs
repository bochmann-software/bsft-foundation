namespace Bsft.Modules;

/// <summary>
/// Manages the lifecycle of loaded modules, coordinating startup and shutdown
/// in the correct dependency order.
/// </summary>
public sealed class ModuleHost : IModuleHost
{
    private readonly List<IModule> _modules;
    private readonly IModuleCatalog _catalog;
    private readonly Lazy<IReadOnlyList<ModuleInfo>> _moduleInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleHost"/> class.
    /// </summary>
    /// <param name="modules">The modules in dependency order (dependencies first).</param>
    /// <param name="catalog">The module catalog for metadata access.</param>
    public ModuleHost(IReadOnlyList<IModule> modules, IModuleCatalog catalog)
    {
        _modules = new List<IModule>(modules ?? throw new ArgumentNullException(nameof(modules)));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _moduleInfo = new Lazy<IReadOnlyList<ModuleInfo>>(BuildModuleInfo);
    }

    /// <summary>
    /// Gets the loaded modules in dependency order.
    /// </summary>
    public IReadOnlyList<IModule> Modules => _modules.AsReadOnly();

    /// <summary>
    /// Gets the module catalog associated with this host.
    /// </summary>
    public IModuleCatalog Catalog => _catalog;

    /// <summary>
    /// Starts all modules by calling <see cref="IModule.OnStartAsync"/> in dependency order.
    /// </summary>
    /// <param name="services">The service provider to pass to each module.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    public async Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        foreach (var module in _modules)
        {
            await module.OnStartAsync(services, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops all modules by calling <see cref="IModule.OnStopAsync"/> in reverse dependency order.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        for (var i = _modules.Count - 1; i >= 0; i--)
        {
            await _modules[i].OnStopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns module metadata suitable for the discovery endpoint.
    /// </summary>
    /// <returns>The name, version and enabled status of every module the catalog knows.</returns>
    /// <remarks>
    /// The set of modules is fixed once the host is built, so the answer is built once and handed out
    /// afterwards rather than projected again for every caller of the discovery endpoint.
    /// </remarks>
    public IReadOnlyList<ModuleInfo> ListModuleInfo() => _moduleInfo.Value;

    /// <summary>Projects the catalog into what the discovery endpoint answers with.</summary>
    /// <returns>The metadata of every module in the catalog.</returns>
    /// <remarks>
    /// A module named by a manifest has no version there to read, and the catalog records a placeholder
    /// for it. The loaded module knows its own version, so it is asked: reporting every manifest module
    /// as <c>0.0.0</c> would make the endpoint useless for exactly the deployments that use manifests.
    /// A disabled module was never loaded and keeps whatever the catalog recorded.
    /// </remarks>
    private IReadOnlyList<ModuleInfo> BuildModuleInfo()
    {
        var loaded = _modules.ToDictionary(module => module.Name, StringComparer.OrdinalIgnoreCase);

        return _catalog.ListModules()
            .Select(descriptor => new ModuleInfo(
                descriptor.Name,
                loaded.TryGetValue(descriptor.Name, out var module) ? module.Version : descriptor.Version,
                descriptor.Enabled))
            .ToList()
            .AsReadOnly();
    }
}
