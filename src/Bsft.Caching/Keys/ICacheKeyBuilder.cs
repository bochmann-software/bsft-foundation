namespace Bsft.Caching.Keys;

/// <summary>
/// Builds the fully qualified cache key for a cacheable request.
/// </summary>
public interface ICacheKeyBuilder
{
    /// <summary>
    /// Builds the key under which the result of <paramref name="request"/> is cached. The request's
    /// runtime type namespaces the key, so two request types sharing a discriminator never collide.
    /// </summary>
    /// <param name="request">The cacheable request supplying its own discriminator.</param>
    /// <returns>
    /// The fully qualified cache key, or <see langword="null"/> when the ambient scope is
    /// <see cref="CacheKeyScope.NotCacheable"/> — there is no key such a request may read from or
    /// write to, so its caller runs the request uncached.
    /// </returns>
    string? Build(ICacheable request);
}
