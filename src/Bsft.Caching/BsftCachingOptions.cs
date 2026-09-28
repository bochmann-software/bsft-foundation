namespace Bsft.Caching;

/// <summary>
/// Configuration for the mediator caching behavior and the cache store behind it.
/// </summary>
public sealed class BsftCachingOptions
{
    /// <summary>
    /// The configuration section this options type binds from: <c>Bsft:Caching</c>.
    /// </summary>
    public const string SectionName = "Bsft:Caching";

    /// <summary>
    /// Gets or sets the cache infrastructure to use. Defaults to <see cref="CacheProvider.Memory"/>.
    /// </summary>
    public CacheProvider Provider { get; set; } = CacheProvider.Memory;

    /// <summary>
    /// Gets or sets the lifetime applied to entries whose request does not specify its own
    /// <see cref="ICacheable.TimeToLive"/>. Defaults to five minutes.
    /// </summary>
    public TimeSpan DefaultTimeToLive { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the prefix every cache key starts with. Keeps the framework's entries apart
    /// from other writers on a shared distributed cache. Defaults to <c>bsft</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "bsft";
}
