using Microsoft.Extensions.Options;

namespace Bsft.Caching.Keys;

/// <summary>
/// Default <see cref="ICacheKeyBuilder"/>. Composes the configured prefix, the ambient scope, the
/// request type's full name and the request's own discriminator, so neither two request types nor
/// two scopes sharing a discriminator can collide.
/// </summary>
public sealed class CacheKeyBuilder : ICacheKeyBuilder
{
    private readonly BsftCachingOptions _options;
    private readonly ICacheKeyScopeProvider _scopeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheKeyBuilder"/> class.
    /// </summary>
    /// <param name="options">The caching options supplying the key prefix.</param>
    /// <param name="scopeProvider">The provider of the ambient scope every key is qualified with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public CacheKeyBuilder(IOptions<BsftCachingOptions> options, ICacheKeyScopeProvider scopeProvider)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
    }

    /// <inheritdoc />
    public string? Build(ICacheable request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var keyScope = _scopeProvider.GetScope();

        // No key at all rather than one built from a substitute discriminator: an ambient context
        // that did not resolve has nothing that separates this request from the next, so every such
        // request would read and write the same entries.
        if (!keyScope.IsCacheable)
            return null;

        var scope = keyScope.Discriminator ?? string.Empty;

        // The assembly-qualified name rather than the full name: two modules may declare the same
        // namespace-qualified query name, and their results must not share a cache entry.
        var requestType = request.GetType().AssemblyQualifiedName!;
        var discriminator = request.CacheKey;

        // Every variable segment is length-prefixed rather than merely separated: a ':' inside a
        // scope or discriminator would otherwise shift the segment boundary, so that two different
        // (scope, type, discriminator) triples could produce one and the same key.
        return $"{_options.KeyPrefix}:{scope.Length}:{scope}:{requestType.Length}:{requestType}:{discriminator.Length}:{discriminator}";
    }
}
