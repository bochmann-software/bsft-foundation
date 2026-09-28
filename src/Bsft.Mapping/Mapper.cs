using System.Linq.Expressions;
using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// Convention-based object mapper that supports in-memory property mapping
/// and expression-tree projection for IQueryable providers such as EF Core.
/// Handles both mutable classes (property setters) and immutable types like
/// records (constructor parameters matched by name).
/// </summary>
public sealed class Mapper : IMapper
{
    private readonly MappingRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="Mapper"/> class by processing
    /// all provided mapping profiles.
    /// </summary>
    /// <param name="profiles">The mapping profiles to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profiles"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when two profiles register the same type pair.
    /// </exception>
    public Mapper(IEnumerable<MappingProfileBase> profiles)
        : this(new MappingRegistry(profiles))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Mapper"/> class over a resolved model.</summary>
    /// <param name="registry">The model the profiles were already read into.</param>
    internal Mapper(MappingRegistry registry) => _registry = registry;

    /// <summary>Gets the mapping model this mapper executes.</summary>
    internal MappingRegistry Registry => _registry;

    /// <inheritdoc />
    public TDest Map<TDest>(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return (TDest)CreateAndPopulate(_registry.Get(source.GetType(), typeof(TDest)), source);
    }

    /// <inheritdoc />
    public TDest Map<TSource, TDest>(TSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return (TDest)CreateAndPopulate(_registry.Get(typeof(TSource), typeof(TDest)), source);
    }

    /// <inheritdoc />
    public TDest Map<TSource, TDest>(TSource source, TDest existing)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existing);

        ApplyMembers(_registry.Get(typeof(TSource), typeof(TDest)), source, existing);

        return existing;
    }

    /// <inheritdoc />
    public Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>()
        => _registry.Get(typeof(TSource), typeof(TDest))
            .Projection<TSource, TDest>(MapperProjections.Build<TSource, TDest>);

    /// <summary>
    /// Creates a new destination object and populates it. For types with a parameterless
    /// constructor, uses property setters. For records/immutable types, resolves values
    /// for constructor parameters by name, then sets any remaining writable properties.
    /// </summary>
    /// <param name="mapping">The mapping for the type pair.</param>
    /// <param name="source">The instance to read from.</param>
    /// <returns>The populated destination instance.</returns>
    private static object CreateAndPopulate(TypePairMapping mapping, object source)
    {
        if (mapping.NewInstance is null)
            return CreateViaConstructor(mapping, source);

        var dest = mapping.NewInstance();
        ApplyMembers(mapping, source, dest);

        return dest;
    }

    /// <summary>Builds an immutable destination by feeding a constructor the mapped values.</summary>
    /// <param name="mapping">The mapping for the type pair.</param>
    /// <param name="source">The instance to read from.</param>
    /// <returns>The constructed destination instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no constructor can be satisfied from the mapped values.
    /// </exception>
    private static object CreateViaConstructor(TypePairMapping mapping, object source)
    {
        var valueMap = BuildValueMap(mapping, source);

        foreach (var candidate in mapping.Constructors)
        {
            if (!TryResolveConstructorArguments(candidate.Parameters, valueMap, out var args))
                continue;

            var dest = candidate.Create(args);

            ApplyMembers(mapping, source, dest, candidate.ParameterNames);

            return dest;
        }

        throw new InvalidOperationException(
            $"Cannot create an instance of '{mapping.DestinationType.Name}'. " +
            "No suitable constructor found. Ensure all constructor parameters match mapped properties by name.");
    }

    /// <summary>Reads a value for every constructor parameter, or reports that one is missing.</summary>
    /// <param name="parameters">The constructor's parameters.</param>
    /// <param name="valueMap">The mapped values by destination member name.</param>
    /// <param name="arguments">The resolved arguments.</param>
    /// <returns><see langword="true"/> when every parameter was resolved.</returns>
    private static bool TryResolveConstructorArguments(
        IReadOnlyList<ParameterInfo> parameters, Dictionary<string, object?> valueMap, out object?[] arguments)
    {
        arguments = new object?[parameters.Count];

        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];

            if (valueMap.TryGetValue(parameter.Name!, out var value))
                arguments[index] = value;
            else if (parameter.HasDefaultValue)
                arguments[index] = parameter.DefaultValue;
            else
                return false;
        }

        return true;
    }

    /// <summary>Reads every mapped member into a lookup the constructor resolution asks by name.</summary>
    /// <param name="mapping">The mapping for the type pair.</param>
    /// <param name="source">The instance to read from.</param>
    /// <returns>The mapped values by destination member name.</returns>
    private static Dictionary<string, object?> BuildValueMap(TypePairMapping mapping, object source)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var accessor in mapping.ConventionAccessors)
            values[accessor.Name] = accessor.Read(source);

        foreach (var accessor in mapping.CustomAccessors)
            values[accessor.Name] = accessor.Read(source);

        return values;
    }

    /// <summary>Assigns every mapped member the destination lets the mapper write.</summary>
    /// <param name="mapping">The mapping for the type pair.</param>
    /// <param name="source">The instance to read from.</param>
    /// <param name="destination">The instance to write to.</param>
    /// <param name="taken">
    /// The member names a constructor already took, which are left alone, or <see langword="null"/>
    /// when the destination was built empty and owes all of them to this call.
    /// </param>
    private static void ApplyMembers(
        TypePairMapping mapping, object source, object destination, IReadOnlySet<string>? taken = null)
    {
        Assign(mapping.ConventionAccessors, source, destination, taken);
        Assign(mapping.CustomAccessors, source, destination, taken);
    }

    /// <summary>Assigns the members of one accessor set.</summary>
    /// <param name="accessors">The accessors to assign through.</param>
    /// <param name="source">The instance to read from.</param>
    /// <param name="destination">The instance to write to.</param>
    /// <param name="taken">The member names to leave alone.</param>
    private static void Assign(
        IReadOnlyList<MemberAccessor> accessors, object source, object destination, IReadOnlySet<string>? taken)
    {
        foreach (var accessor in accessors)
        {
            if (accessor.Write is null || taken?.Contains(accessor.Name) == true)
                continue;

            accessor.Write(destination, accessor.Read(source));
        }
    }
}
