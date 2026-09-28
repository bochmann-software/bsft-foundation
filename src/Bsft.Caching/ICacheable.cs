
namespace Bsft.Caching;

/// <summary>
/// Opt-in marker for mediator queries whose result may be cached.
/// </summary>
/// <remarks>
/// <para>
/// Apply this only to queries that are free of side effects and whose result is fully determined
/// by <see cref="CacheKey"/>. The marker is honoured for queries only: a command carrying it is
/// executed normally, because skipping a command handler would drop its write together with every
/// check it performs.
/// </para>
/// <para>
/// Any request-specific discriminator that is not already supplied by the registered
/// <see cref="Keys.ICacheKeyScopeProvider"/> — filter arguments, and tenant or user identity when no
/// scope provider is configured — must be part of <see cref="CacheKey"/>, otherwise callers will
/// observe each other's results. Keep the discriminator's cardinality bounded and never build it
/// from unfiltered user input: every distinct value becomes its own cache entry.
/// </para>
/// </remarks>
public interface ICacheable
{
    /// <summary>
    /// Gets the discriminator that identifies this request's result within its request type.
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    /// Gets how long the result stays cached, or <see langword="null"/> to use the configured
    /// default. A value of zero or less disables caching for this request.
    /// </summary>
    TimeSpan? TimeToLive => null;

    /// <summary>
    /// Gets the tags this result belongs to. Invalidating a tag evicts every entry carrying it,
    /// which lets a write invalidate a whole family of queries without knowing their keys.
    /// </summary>
    IReadOnlyCollection<string> CacheTags => [];
}
