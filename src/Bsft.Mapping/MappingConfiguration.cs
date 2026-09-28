using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// Fluent configuration class for discovering and registering mapping profiles.
/// </summary>
/// <remarks>
/// A builder-configuration value rather than an options type: it exists only for the duration of the
/// <c>AddBsftMapping</c> call, carries assemblies to scan rather than settings, and is never
/// registered in the container. It therefore has no <c>SectionName</c> and no validator, and
/// <c>AddBsftMapping</c> has no <c>IConfiguration</c> overload — an assembly to scan is not a value a
/// configuration provider can supply.
/// </remarks>
public sealed class MappingConfiguration
{
    internal List<Assembly> Assemblies { get; } = [];

    /// <summary>
    /// Scans the specified assembly for types deriving from <see cref="MappingProfileBase"/>
    /// and registers them for use by the mapper.
    /// </summary>
    /// <param name="assembly">The assembly to scan for mapping profiles.</param>
    /// <returns>The current configuration instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is null.</exception>
    /// <remarks>
    /// Naming an assembly twice scans it once. A host assembling this from several modules can
    /// easily name one of them twice, and every profile in it would then register its type pairs a
    /// second time, which the mapper refuses.
    /// </remarks>
    public MappingConfiguration AddProfilesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!Assemblies.Contains(assembly))
            Assemblies.Add(assembly);

        return this;
    }
}
