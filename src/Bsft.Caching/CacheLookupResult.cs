namespace Bsft.Caching;

/// <summary>
/// The outcome of a cache lookup. Distinguishes a miss from a hit explicitly, so a cached
/// default value (<c>0</c>, an empty string) is not mistaken for an absent entry. A value type,
/// because a lookup happens on every cacheable request.
/// </summary>
/// <typeparam name="T">The type of the cached value.</typeparam>
/// <param name="IsHit">Whether an entry was present.</param>
/// <param name="Value">The cached value when <paramref name="IsHit"/> is <see langword="true"/>; otherwise the default.</param>
public readonly record struct CacheLookupResult<T>(bool IsHit, T? Value);
