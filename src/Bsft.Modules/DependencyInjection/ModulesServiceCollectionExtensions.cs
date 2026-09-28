using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.DependencyInjection;

/// <summary>
/// Extension methods for registering the BSFT module system into the dependency injection container.
/// </summary>
public static class ModulesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BSFT module system. Builds the catalog, resolves dependencies, loads assemblies,
    /// calls <see cref="IModule.ConfigureServices"/> on each module in order, and registers
    /// <see cref="IModuleHost"/> and <see cref="IModuleCatalog"/> as singletons.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configure">An action to configure module sources.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBsftModules(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulesConfiguration> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var modulesConfig = new ModulesConfiguration();
        configure(modulesConfig);

        var catalog = new ModuleCatalog();
        modulesConfig.Apply(catalog);

        var loader = new ModuleLoader();
        loader.LoadModules(catalog, services, configuration);

        var host = new ModuleHost(loader.LoadedModules, catalog);

        services.AddSingleton<IModuleCatalog>(catalog);
        services.AddSingleton<IModuleHost>(host);

        return services;
    }
}
