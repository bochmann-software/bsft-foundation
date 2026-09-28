namespace Bsft.Mediator.StartupValidation;

/// <summary>
/// The one reflection pass over the assemblies a host registered with the mediator. Every startup
/// check reads its types from here, so the assemblies and their <see cref="Type.GetInterfaces"/>
/// are walked once per application start instead of once per validator.
/// </summary>
/// <remarks>
/// The scanner is registered as a singleton and caches the result of its first pass. Nothing loads
/// new types into an assembly after startup, so the cache cannot go stale within the lifetime that
/// matters here. The pass itself is deferred through a <see cref="Lazy{T}"/> in publication mode, so
/// two validators resolved on different threads share one scan instead of racing into two.
/// </remarks>
internal sealed class MediatorTypeScanner
{
    private readonly MediatorConfiguration _config;
    private readonly Lazy<IReadOnlyList<ScannedType>> _types;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorTypeScanner"/> class.
    /// </summary>
    /// <param name="configuration">The mediator configuration holding the assemblies to scan.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is null.</exception>
    public MediatorTypeScanner(MediatorConfiguration configuration)
    {
        _config = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _types = new Lazy<IReadOnlyList<ScannedType>>(Scan, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// The types of every configured assembly, each with its interfaces, scanned on first call and
    /// cached afterwards.
    /// </summary>
    /// <returns>The scanned types, in assembly order.</returns>
    public IReadOnlyList<ScannedType> ScanTypes() => _types.Value;

    private IReadOnlyList<ScannedType> Scan()
    {
        var scanned = new List<ScannedType>();

        foreach (var assembly in _config.Assemblies)
        {
            foreach (var type in AssemblyTypes.Loadable(assembly))
            {
                scanned.Add(new ScannedType(type, type.GetInterfaces()));
            }
        }

        return scanned;
    }

}
