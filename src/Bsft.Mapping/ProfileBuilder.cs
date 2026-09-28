namespace Bsft.Mapping;

/// <summary>
/// Builder used within <see cref="MappingProfileBase.Configure"/> to register type-pair mappings.
/// </summary>
public sealed class ProfileBuilder
{
    private readonly Dictionary<TypePair, IMapBuilder> _mapBuilders = new();

    /// <summary>
    /// Gets all registered map builders keyed by their source/destination type pair.
    /// </summary>
    internal IReadOnlyDictionary<TypePair, IMapBuilder> MapBuilders => _mapBuilders;

    /// <summary>
    /// Registers a mapping from <typeparamref name="TSource"/> to <typeparamref name="TDest"/>
    /// and returns a builder for configuring member-level overrides.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <returns>A <see cref="MapBuilder{TSource, TDest}"/> for further configuration.</returns>
    public MapBuilder<TSource, TDest> CreateMap<TSource, TDest>()
    {
        var key = new TypePair(typeof(TSource), typeof(TDest));

        if (_mapBuilders.ContainsKey(key))
            throw new InvalidOperationException(
                $"A mapping from '{typeof(TSource).Name}' to '{typeof(TDest).Name}' has already been registered.");

        var builder = new MapBuilder<TSource, TDest>();
        _mapBuilders[key] = builder;
        return builder;
    }
}
