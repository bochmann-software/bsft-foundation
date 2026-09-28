using System.Linq.Expressions;
using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// The resolved mapping for a single source-to-destination type pair: the convention-matched
/// properties, the explicit overrides, the ignored members, and everything executing them needs.
/// </summary>
/// <remarks>
/// Everything a <c>Map</c> call would otherwise ask reflection for is resolved once, here, when the
/// mapping is registered: the calls it reads and writes through, the constructors it may use and
/// the projection it hands a query provider. A mapper is a singleton whose calls sit on the path of
/// every request that touches it, and none of these answers can change after registration.
/// </remarks>
internal sealed class TypePairMapping
{
    private readonly object _projectionLock = new();
    private object? _projection;

    /// <summary>Initializes a new instance of the <see cref="TypePairMapping"/> class.</summary>
    /// <param name="pair">The source and destination types this mapping is for.</param>
    /// <param name="conventionMappings">Convention-matched property pairs.</param>
    /// <param name="customMappings">Explicitly configured member mappings.</param>
    /// <param name="ignoredMembers">Destination property names that are explicitly ignored.</param>
    public TypePairMapping(
        TypePair pair,
        IReadOnlyList<(PropertyInfo DestProp, PropertyInfo SourceProp)> conventionMappings,
        IReadOnlyDictionary<string, LambdaExpression> customMappings,
        IReadOnlySet<string> ignoredMembers)
    {
        Pair = pair;
        ConventionMappings = conventionMappings;
        CustomMappings = customMappings;
        IgnoredMembers = ignoredMembers;

        ConventionAccessors = conventionMappings
            .Select(pair => new MemberAccessor(
                pair.DestProp.Name, CompileGetter(pair.SourceProp), CompileSetter(pair.DestProp)))
            .ToArray();

        CustomTargets = customMappings.ToDictionary(
            kvp => kvp.Key, kvp => SettableProperty(pair.Destination, kvp.Key));

        CustomAccessors = customMappings
            .Select(kvp => new MemberAccessor(kvp.Key, Box(kvp.Value), CompileSetter(CustomTargets[kvp.Key])))
            .ToArray();

        NewInstance = CompileParameterlessNew(pair.Destination);

        Constructors = pair.Destination.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(Candidate)
            .OrderByDescending(candidate => candidate.Parameters.Count)
            .ToArray();
    }

    /// <summary>Gets the source and destination types this mapping is for.</summary>
    public TypePair Pair { get; }

    /// <summary>Gets the source type for this mapping.</summary>
    public Type SourceType => Pair.Source;

    /// <summary>Gets the destination type for this mapping.</summary>
    public Type DestinationType => Pair.Destination;

    /// <summary>Gets the convention-matched property pairs (destination property to source property).</summary>
    public IReadOnlyList<(PropertyInfo DestProp, PropertyInfo SourceProp)> ConventionMappings { get; }

    /// <summary>Gets the explicitly configured member mappings (destination member name to source expression).</summary>
    /// <remarks>
    /// Retained uncompiled for expression-tree projections, which need the expression rather than a
    /// delegate built from it. In-memory mapping reads <see cref="CustomAccessors"/> instead.
    /// </remarks>
    public IReadOnlyDictionary<string, LambdaExpression> CustomMappings { get; }

    /// <summary>Gets the destination property names that are explicitly ignored.</summary>
    public IReadOnlySet<string> IgnoredMembers { get; }

    /// <summary>
    /// Gets one reader, and where the destination allows it one writer, per convention-matched member.
    /// </summary>
    /// <remarks>
    /// Compiled rather than called through <see cref="PropertyInfo"/>, which resolves the accessor
    /// and boxes its arguments again for every property of every mapped instance.
    /// </remarks>
    public IReadOnlyList<MemberAccessor> ConventionAccessors { get; }

    /// <summary>Gets one reader, and where the destination allows it one writer, per explicit mapping.</summary>
    public IReadOnlyList<MemberAccessor> CustomAccessors { get; }

    /// <summary>
    /// Gets the destination property each explicit mapping writes to, or <see langword="null"/> when
    /// the destination carries no property of that name the mapper may assign.
    /// </summary>
    public IReadOnlyDictionary<string, PropertyInfo?> CustomTargets { get; }

    /// <summary>
    /// Gets the call that builds an instance through the destination's public parameterless
    /// constructor, or <see langword="null"/> when it has none and has to be built through one
    /// that takes its values.
    /// </summary>
    public Func<object>? NewInstance { get; }

    /// <summary>Gets the destination's public constructors, widest first.</summary>
    public IReadOnlyList<ConstructorCandidate> Constructors { get; }

    /// <summary>Returns the projection for this pair, building it on the first call.</summary>
    /// <typeparam name="TSource">The source type, which is <see cref="SourceType"/>.</typeparam>
    /// <typeparam name="TDest">The destination type, which is <see cref="DestinationType"/>.</typeparam>
    /// <param name="build">Builds the projection when there is not one yet.</param>
    /// <returns>The projection expression.</returns>
    /// <remarks>
    /// A projection is an expression tree over two types that cannot change, so building it per call
    /// rebuilds the same tree once per query, on the path a repository reads its lists through.
    /// </remarks>
    public Expression<Func<TSource, TDest>> Projection<TSource, TDest>(
        Func<TypePairMapping, Expression<Func<TSource, TDest>>> build)
    {
        lock (_projectionLock)
        {
            _projection ??= build(this);

            return (Expression<Func<TSource, TDest>>)_projection;
        }
    }

    /// <summary>Wraps a typed mapping expression as a call taking and returning <see cref="object"/>.</summary>
    /// <param name="mapping">The configured mapping expression.</param>
    /// <returns>The compiled call.</returns>
    private static Func<object, object?> Box(LambdaExpression mapping)
    {
        var source = Expression.Parameter(typeof(object), "source");
        var typedSource = Expression.Convert(source, mapping.Parameters[0].Type);
        var result = Expression.Convert(Expression.Invoke(mapping, typedSource), typeof(object));

        return Expression.Lambda<Func<object, object?>>(result, source).Compile();
    }

    /// <summary>Compiles a read of one source property.</summary>
    /// <param name="property">The property to read.</param>
    /// <returns>The compiled call.</returns>
    private static Func<object, object?> CompileGetter(PropertyInfo property)
    {
        var source = Expression.Parameter(typeof(object), "source");
        var access = Expression.Property(Expression.Convert(source, property.DeclaringType!), property);

        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(access, typeof(object)), source).Compile();
    }

    /// <summary>Compiles an assignment to one destination property.</summary>
    /// <param name="property">The property to assign, or <see langword="null"/> when there is none.</param>
    /// <returns>The compiled call, or <see langword="null"/> when the mapper may not assign it.</returns>
    private static Action<object, object?>? CompileSetter(PropertyInfo? property)
    {
        if (property is null || !DestinationProperties.IsSettable(property))
            return null;

        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var assign = Expression.Assign(
            Expression.Property(Expression.Convert(target, property.DeclaringType!), property),
            Expression.Convert(value, property.PropertyType));

        return Expression.Lambda<Action<object, object?>>(assign, target, value).Compile();
    }

    /// <summary>Compiles a call to the destination's public parameterless constructor.</summary>
    /// <param name="destinationType">The destination type.</param>
    /// <returns>The compiled call, or <see langword="null"/> when there is no such constructor.</returns>
    /// <remarks>
    /// Calling the constructor through <see cref="ConstructorInfo.Invoke(object?[])"/> would wrap
    /// whatever the destination throws in a <see cref="TargetInvocationException"/>, so a destination
    /// that validates in its constructor would reach the caller as something else entirely.
    /// </remarks>
    private static Func<object>? CompileParameterlessNew(Type destinationType)
    {
        var ctor = destinationType.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null);

        return ctor is null
            ? null
            : Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object))).Compile();
    }

    /// <summary>Resolves everything calling one constructor takes.</summary>
    /// <param name="constructor">The constructor.</param>
    /// <returns>The candidate.</returns>
    private static ConstructorCandidate Candidate(ConstructorInfo constructor)
    {
        var parameters = constructor.GetParameters();
        var names = parameters.Select(parameter => parameter.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ConstructorCandidate(constructor, parameters, names, CompileNew(constructor));
    }

    /// <summary>Compiles a call to one constructor, taking its arguments boxed in an array.</summary>
    /// <param name="constructor">The constructor to call.</param>
    /// <returns>The compiled call.</returns>
    private static Func<object?[], object> CompileNew(ConstructorInfo constructor)
    {
        var args = Expression.Parameter(typeof(object?[]), "args");
        var parameters = constructor.GetParameters();
        var arguments = new Expression[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            arguments[index] = Expression.Convert(
                Expression.ArrayIndex(args, Expression.Constant(index)), parameters[index].ParameterType);
        }

        return Expression.Lambda<Func<object?[], object>>(
            Expression.Convert(Expression.New(constructor, arguments), typeof(object)), args).Compile();
    }

    /// <summary>Reads the destination property an explicit mapping writes to.</summary>
    /// <param name="destinationType">The destination type.</param>
    /// <param name="name">The destination property's name.</param>
    /// <returns>The property, or <see langword="null"/> when it is absent or cannot be assigned.</returns>
    private static PropertyInfo? SettableProperty(Type destinationType, string name)
    {
        var property = destinationType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        return property is not null && DestinationProperties.IsSettable(property) ? property : null;
    }
}
