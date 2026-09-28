using System.Linq.Expressions;
using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// The mapping model a set of profiles describes: one <see cref="TypePairMapping"/> per type pair,
/// resolved once and then only read.
/// </summary>
/// <remarks>
/// Kept apart from the mapper so that building the model and executing a map are two jobs held by
/// two types, and so that validating the model does not mean reaching into the mapper for it.
/// </remarks>
internal sealed class MappingRegistry
{
    private readonly Dictionary<TypePair, TypePairMapping> _mappings = new();

    /// <summary>Initializes a new instance of the <see cref="MappingRegistry"/> class.</summary>
    /// <param name="profiles">The mapping profiles to read.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profiles"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when two profiles register the same type pair.
    /// </exception>
    public MappingRegistry(IEnumerable<MappingProfileBase> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        foreach (var profile in profiles)
        {
            var builder = new ProfileBuilder();
            profile.Configure(builder);

            foreach (var (pair, mapBuilder) in builder.MapBuilders)
                Add(pair, mapBuilder, profile);
        }
    }

    /// <summary>Gets every registered mapping.</summary>
    public IReadOnlyCollection<TypePairMapping> Mappings => _mappings.Values;

    /// <summary>Gets the number of registered type pairs.</summary>
    public int Count => _mappings.Count;

    /// <summary>Reads the mapping for one type pair.</summary>
    /// <param name="sourceType">The source type.</param>
    /// <param name="destinationType">The destination type.</param>
    /// <returns>The mapping.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the pair was never registered.</exception>
    public TypePairMapping Get(Type sourceType, Type destinationType)
    {
        if (_mappings.TryGetValue(new TypePair(sourceType, destinationType), out var mapping))
            return mapping;

        throw new InvalidOperationException(
            $"No mapping configured from '{sourceType.Name}' to '{destinationType.Name}'. " +
            "Register a mapping in a MappingProfileBase.");
    }

    /// <summary>Resolves one registered type pair and records it.</summary>
    /// <param name="pair">The source and destination types.</param>
    /// <param name="mapBuilder">What the profile configured for the pair.</param>
    /// <param name="profile">The profile that configured it, named if the pair is a duplicate.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an earlier profile already registered the pair.
    /// </exception>
    /// <remarks>
    /// A profile refuses to register a pair twice, but two profiles could each register it once and
    /// the second would simply replace the first. Which of them won depended on the order the
    /// assembly scan happened to return the profiles in, so the mapping a host got was not the
    /// mapping it could read off its own configuration.
    /// </remarks>
    private void Add(TypePair pair, IMapBuilder mapBuilder, MappingProfileBase profile)
    {
        if (_mappings.ContainsKey(pair))
        {
            throw new InvalidOperationException(
                $"A mapping from '{pair.Source.Name}' to '{pair.Destination.Name}' is registered by more than one " +
                $"profile, most recently by '{profile.GetType().Name}'. Register each type pair once.");
        }

        var customMappings = mapBuilder.CustomMappings;
        var ignoredMembers = mapBuilder.IgnoredMembers;

        _mappings[pair] = new TypePairMapping(
            pair,
            ConventionMappings(pair.Source, pair.Destination, customMappings, ignoredMembers),
            customMappings,
            ignoredMembers);
    }

    /// <summary>Matches the destination's properties to same-named, type-compatible source ones.</summary>
    /// <param name="sourceType">The source type.</param>
    /// <param name="destinationType">The destination type.</param>
    /// <param name="customMappings">The members the profile configured explicitly.</param>
    /// <param name="ignoredMembers">The members the profile left unmapped on purpose.</param>
    /// <returns>The matched pairs.</returns>
    /// <remarks>
    /// Properties without a public setter stay in the result: an immutable destination takes them
    /// through its constructor, and it is the write paths, not the match, that decide what may be
    /// assigned.
    /// </remarks>
    private static IReadOnlyList<(PropertyInfo DestProp, PropertyInfo SourceProp)> ConventionMappings(
        Type sourceType,
        Type destinationType,
        IReadOnlyDictionary<string, LambdaExpression> customMappings,
        IReadOnlySet<string> ignoredMembers)
    {
        var result = new List<(PropertyInfo, PropertyInfo)>();

        foreach (var destProp in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (customMappings.ContainsKey(destProp.Name) || ignoredMembers.Contains(destProp.Name))
                continue;

            var sourceProp = sourceType.GetProperty(destProp.Name, BindingFlags.Public | BindingFlags.Instance);

            if (sourceProp is null || !sourceProp.CanRead)
                continue;

            if (!destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                continue;

            result.Add((destProp, sourceProp));
        }

        return result;
    }
}
