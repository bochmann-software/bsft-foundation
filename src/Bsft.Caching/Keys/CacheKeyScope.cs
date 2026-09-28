namespace Bsft.Caching.Keys;

/// <summary>
/// The ambient scope a cache key is qualified with, or the explicit decision that the current
/// request must not touch the cache at all.
/// </summary>
/// <remarks>
/// The third state is what a nullable string could not express: an ambient context that failed to
/// resolve has no scope to fold in, and pooling every such request under one substitute discriminator
/// would give them a shared bucket — one request's result served to the next. Saying "not cacheable"
/// instead makes <c>CachingBehavior</c> skip the read and the write, so nothing is shared and nothing
/// is written.
/// <para>
/// A value type, because a scope is asked for on every cacheable request. <see langword="default"/> is
/// <see cref="NotCacheable"/> deliberately: a provider that returns an uninitialized value disables
/// caching, which is loud and safe, rather than silently caching globally.
/// </para>
/// </remarks>
public readonly struct CacheKeyScope : IEquatable<CacheKeyScope>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheKeyScope"/> struct.
    /// </summary>
    /// <param name="isCacheable">Whether the request may be cached at all.</param>
    /// <param name="discriminator">The discriminator every key is qualified with, if any.</param>
    private CacheKeyScope(bool isCacheable, string? discriminator)
    {
        IsCacheable = isCacheable;
        Discriminator = discriminator;
    }

    /// <summary>
    /// Gets the scope of a host that caches globally: cacheable, qualified by nothing.
    /// </summary>
    public static CacheKeyScope Global => new(true, null);

    /// <summary>
    /// Gets the scope of a request that must not be cached, so that neither a cache read nor a cache
    /// write happens for it.
    /// </summary>
    public static CacheKeyScope NotCacheable => new(false, null);

    /// <summary>
    /// Gets a value indicating whether the request may be served from and written to the cache.
    /// </summary>
    public bool IsCacheable { get; }

    /// <summary>
    /// Gets the discriminator every cache key is qualified with, or <see langword="null"/> when the
    /// cache is global. Always <see langword="null"/> while <see cref="IsCacheable"/> is
    /// <see langword="false"/>.
    /// </summary>
    public string? Discriminator { get; }

    /// <summary>
    /// Creates a cacheable scope qualified by <paramref name="discriminator"/>, so results cached
    /// under it are served to that scope only.
    /// </summary>
    /// <param name="discriminator">The discriminator, for example the resolved tenant.</param>
    /// <returns>The scope carrying <paramref name="discriminator"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="discriminator"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="discriminator"/> is blank.</exception>
    /// <remarks>
    /// A blank discriminator is refused rather than carried. The key builder folds an absent one to the
    /// empty string, so a scope carrying "" would qualify its keys exactly as <see cref="Global"/> does
    /// and share a bucket with every other caller whose discriminator failed to resolve — which is the
    /// one thing this type exists to prevent. A provider that has nothing to name the caller with says
    /// <see cref="NotCacheable"/>.
    /// </remarks>
    public static CacheKeyScope For(string discriminator)
    {
        ArgumentNullException.ThrowIfNull(discriminator);

        if (string.IsNullOrWhiteSpace(discriminator))
        {
            throw new ArgumentException(
                "A cache key scope needs something to tell one caller from another. A blank discriminator "
                + "qualifies nothing and shares the global bucket; return NotCacheable instead.",
                nameof(discriminator));
        }

        return new CacheKeyScope(true, discriminator);
    }

    /// <summary>Compares two scopes for equality.</summary>
    /// <param name="left">The left scope.</param>
    /// <param name="right">The right scope.</param>
    /// <returns><see langword="true"/> when both scopes are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(CacheKeyScope left, CacheKeyScope right) => left.Equals(right);

    /// <summary>Compares two scopes for inequality.</summary>
    /// <param name="left">The left scope.</param>
    /// <param name="right">The right scope.</param>
    /// <returns><see langword="true"/> when the scopes differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(CacheKeyScope left, CacheKeyScope right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(CacheKeyScope other) =>
        IsCacheable == other.IsCacheable && string.Equals(Discriminator, other.Discriminator, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CacheKeyScope other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(IsCacheable, Discriminator is null ? 0 : StringComparer.Ordinal.GetHashCode(Discriminator));

    /// <inheritdoc />
    public override string ToString() =>
        IsCacheable ? Discriminator ?? "<global>" : "<not cacheable>";
}
