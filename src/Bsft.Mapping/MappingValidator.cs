using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// Validates that all registered mapping configurations are complete by ensuring every
/// settable destination property is either convention-matched, explicitly mapped, or explicitly ignored.
/// </summary>
public sealed class MappingValidator : IMappingValidationService
{
    private readonly MappingRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="MappingValidator"/> class.
    /// </summary>
    /// <param name="mapper">The mapper whose configurations should be validated.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapper"/> is null.</exception>
    public MappingValidator(Mapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        _registry = mapper.Registry;
    }

    /// <summary>
    /// Returns the number of registered type-pair mappings.
    /// </summary>
    /// <returns>The number of registered type pairs.</returns>
    public int GetProfileCount() => _registry.Count;

    /// <summary>
    /// Validates all registered mappings and throws a <see cref="MappingValidationException"/>
    /// if any destination properties are unmapped, not explicitly configured, or not ignored.
    /// </summary>
    /// <exception cref="MappingValidationException">
    /// Thrown when one or more destination properties lack a mapping configuration.
    /// </exception>
    /// <remarks>
    /// Only the properties the mapper is allowed to assign are required to be configured. A property
    /// the destination keeps to itself is not something a profile could have mapped, so demanding a
    /// configuration for it would fail a start-up over a member no mapping can ever write.
    /// </remarks>
    public void Validate()
    {
        var errors = new List<string>();

        foreach (var mapping in _registry.Mappings)
            CollectUnmapped(mapping, errors);

        if (errors.Count > 0)
            throw new MappingValidationException(errors);
    }

    /// <summary>Records every destination property of one mapping that nothing accounts for.</summary>
    /// <param name="mapping">The mapping to check.</param>
    /// <param name="errors">The messages collected so far.</param>
    private static void CollectUnmapped(TypePairMapping mapping, List<string> errors)
    {
        var conventionNames = mapping.ConventionMappings.Select(m => m.DestProp.Name).ToHashSet();

        foreach (var destProp in mapping.DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!DestinationProperties.IsSettable(destProp))
                continue;

            if (conventionNames.Contains(destProp.Name)
                || mapping.CustomMappings.ContainsKey(destProp.Name)
                || mapping.IgnoredMembers.Contains(destProp.Name))
                continue;

            errors.Add(
                $"'{mapping.SourceType.Name}' -> '{mapping.DestinationType.Name}': " +
                $"destination property '{destProp.Name}' is not mapped, not explicitly configured, and not ignored.");
        }
    }
}
