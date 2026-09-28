namespace Bsft.Instrumentation;

/// <summary>
/// Resolves named <see cref="IInstrumentationSource"/> instances. Each distinct name maps to a
/// single cached source for the lifetime of the provider.
/// </summary>
public interface IInstrumentationSourceProvider
{
    /// <summary>
    /// Gets the instrumentation source for <paramref name="name"/>, creating and caching it on
    /// first access.
    /// </summary>
    /// <param name="name">The source name (must be non-empty).</param>
    /// <returns>The cached instrumentation source for the name.</returns>
    /// <remarks>
    /// A source is kept for the life of the provider, so the set of names is one a deployment decides
    /// at startup - a package name, a module name - and never one built from a request. A name
    /// carrying a tenant or a user would accumulate a live activity source and meter per value, for
    /// the life of the process.
    /// </remarks>
    IInstrumentationSource Get(string name);
}
