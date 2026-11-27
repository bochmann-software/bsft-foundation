namespace Bsft.Modules;

/// <summary>
/// Fluent configuration builder for the BSFT module system.
/// Collects module sources that will be applied to the <see cref="ModuleCatalog"/> during registration.
/// </summary>
/// <remarks>
/// A builder-configuration value rather than an options type: it exists only for the duration of the
/// <c>AddBsftModules</c> call, carries sources to read rather than settings, and is never registered
/// in the container, so it has no <c>SectionName</c> and no validator. <c>AddBsftModules</c> already
/// takes the application's <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> — it is
/// handed to each module's <c>ConfigureServices</c>, not bound into these settings.
/// </remarks>
public sealed class ModulesConfiguration
{
    private readonly List<Action<ModuleCatalog>> _configActions = [];

    /// <summary>
    /// Reads module descriptors from a JSON configuration file.
    /// </summary>
    /// <param name="path">The path to the JSON configuration file.</param>
    /// <returns>This configuration instance for fluent chaining.</returns>
    public ModulesConfiguration FromConfigurationFile(string path)
    {
        _configActions.Add(catalog => catalog.FromConfigurationFile(path));
        return this;
    }

    /// <summary>
    /// Scans a directory for assemblies containing <see cref="IModule"/> implementations.
    /// </summary>
    /// <param name="path">The path to the directory to scan.</param>
    /// <returns>This configuration instance for fluent chaining.</returns>
    public ModulesConfiguration FromDirectory(string path)
    {
        _configActions.Add(catalog => catalog.FromDirectory(path));
        return this;
    }

    /// <summary>
    /// Registers a module by its CLR type.
    /// </summary>
    /// <typeparam name="TModule">The type implementing <see cref="IModule"/>.</typeparam>
    /// <returns>This configuration instance for fluent chaining.</returns>
    public ModulesConfiguration Add<TModule>() where TModule : class, IModule
    {
        _configActions.Add(catalog => catalog.Add<TModule>());
        return this;
    }

    /// <summary>
    /// Registers a module by a CLR type known only at run time, such as one a host discovered by scanning
    /// the assemblies it was built with, instead of naming each module in code.
    /// </summary>
    /// <param name="moduleType">
    /// A concrete class implementing <see cref="IModule"/> with a public parameterless constructor.
    /// </param>
    /// <returns>This configuration instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="moduleType"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="moduleType"/> is not such a type. Checked here rather than when the
    /// catalog is built, so that the failure points at the call that passed the type.
    /// </exception>
    public ModulesConfiguration Add(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        ModuleCatalog.RequireModuleType(moduleType);

        _configActions.Add(catalog => catalog.Add(moduleType));
        return this;
    }

    /// <summary>
    /// Applies all collected configuration actions to the given catalog.
    /// </summary>
    /// <param name="catalog">The catalog to configure.</param>
    internal void Apply(ModuleCatalog catalog)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        foreach (var action in _configActions)
        {
            action(catalog);
        }
    }
}
