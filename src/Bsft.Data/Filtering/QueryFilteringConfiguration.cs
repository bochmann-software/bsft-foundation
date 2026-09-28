using System.Reflection;


namespace Bsft.Data.Filtering;

/// <summary>
/// Configuration object for the query filtering system, used during DI registration.
/// </summary>
public sealed class QueryFilteringConfiguration
{
    private readonly List<Assembly> _assemblies = [];

    /// <summary>
    /// Registers an assembly to scan for <see cref="QueryConfigurationBase{TEntity, TDto}"/> implementations.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>This configuration for chaining.</returns>
    public QueryFilteringConfiguration FromAssembly(Assembly assembly)
    {
        if (assembly is null)
            throw new ArgumentNullException(nameof(assembly));

        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Gets the list of assemblies to scan for query configurations.
    /// </summary>
    internal IReadOnlyList<Assembly> Assemblies => _assemblies;
}
