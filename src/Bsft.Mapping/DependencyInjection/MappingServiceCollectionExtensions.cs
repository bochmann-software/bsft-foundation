using System.Reflection;
using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mapping.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register BSFT mapping services.
/// </summary>
public static class MappingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BSFT convention-based mapper with the service collection as a singleton.
    /// Discovers all <see cref="MappingProfileBase"/> implementations from the configured assemblies,
    /// builds the mapper, and validates all mappings at startup.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configure">An action to configure the mapping by specifying assemblies to scan.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddBsftMapping(
        this IServiceCollection services,
        Action<MappingConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new MappingConfiguration();
        configure(configuration);

        var profiles = new List<MappingProfileBase>();

        foreach (var assembly in configuration.Assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                if (!typeof(MappingProfileBase).IsAssignableFrom(type))
                    continue;

                var profile = (MappingProfileBase)Activator.CreateInstance(type)!;
                profiles.Add(profile);
            }
        }

        var mapper = new Mapper(profiles);

        services.AddSingleton<IMapper>(mapper);

        // Validation reads the model this call built, not whatever IMapper the container ends up
        // resolving: a host registering its own mapper after this call replaces the execution, and
        // the profiles discovered here are still what its configuration has to be complete against.
        services.AddSingleton<IMappingValidationService>(new MappingValidator(mapper));
        services.AddTransient<IStartupValidator, MappingStartupValidator>();

        return services;
    }
}
