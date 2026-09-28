namespace Bsft.Caching.Keys;

/// <summary>
/// Supplies the ambient scope every cache key is qualified with, so results cached for one
/// tenant or user are never served to another.
/// </summary>
/// <remarks>
/// The default implementation returns <see cref="CacheKeyScope.Global"/>, which caches globally. An
/// application whose queries are tenant- or user-specific should register an implementation reading
/// its own ambient context, rather than relying on every query author to fold the identity into
/// <see cref="ICacheable.CacheKey"/> by hand.
/// <para>
/// A provider whose ambient context did not resolve returns <see cref="CacheKeyScope.NotCacheable"/>
/// rather than inventing a substitute discriminator: every request sharing such a substitute would
/// share its cache entries too.
/// </para>
/// </remarks>
public interface ICacheKeyScopeProvider
{
    /// <summary>
    /// Returns the current scope, which is either cacheable — global or carrying a discriminator —
    /// or <see cref="CacheKeyScope.NotCacheable"/>.
    /// </summary>
    /// <returns>The current scope.</returns>
    CacheKeyScope GetScope();
}
