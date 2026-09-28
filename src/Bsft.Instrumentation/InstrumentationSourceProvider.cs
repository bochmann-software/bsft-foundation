using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Bsft.Instrumentation;

/// <summary>
/// Default <see cref="IInstrumentationSourceProvider"/> implementation that caches one
/// <see cref="InstrumentationSource"/> per name.
/// </summary>
internal sealed class InstrumentationSourceProvider : IInstrumentationSourceProvider, IDisposable
{
    private readonly IMeterFactory _meterFactory;

    /// <summary>
    /// The sources handed out so far, one per name.
    /// </summary>
    /// <remarks>
    /// Held behind a <see cref="Lazy{T}"/> because a concurrent dictionary runs its add factory
    /// outside any lock: two callers asking for the same new name can both build a source, and only
    /// one of them is kept. The discarded one has already constructed an
    /// <see cref="System.Diagnostics.ActivitySource"/>, which registers itself with the runtime and
    /// stays registered until it is disposed - and nothing holds it to dispose it. The lazy makes the
    /// construction itself happen once, so there is no second source to lose.
    /// </remarks>
    private readonly ConcurrentDictionary<string, Lazy<InstrumentationSource>> _sources =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="InstrumentationSourceProvider"/> class.
    /// </summary>
    /// <param name="meterFactory">The factory used to create meters for each source.</param>
    public InstrumentationSourceProvider(IMeterFactory meterFactory)
    {
        _meterFactory = meterFactory ?? throw new ArgumentNullException(nameof(meterFactory));
    }

    /// <inheritdoc />
    public IInstrumentationSource Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(ObservabilitySourceRegistration.NameRequiredMessage, nameof(name));

        return _sources
            .GetOrAdd(
                name,
                static (n, factory) => new Lazy<InstrumentationSource>(() => new InstrumentationSource(n, factory)),
                _meterFactory)
            .Value;
    }

    /// <summary>Disposes all cached instrumentation sources.</summary>
    public void Dispose()
    {
        foreach (var source in _sources.Values)
        {
            if (source.IsValueCreated)
                source.Value.Dispose();
        }
    }
}
