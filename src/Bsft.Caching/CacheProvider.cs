namespace Bsft.Caching;

/// <summary>
/// Selects which cache infrastructure the caching behavior writes to.
/// </summary>
public enum CacheProvider
{
    /// <summary>
    /// The in-process memory cache. Fast, but not shared between application instances.
    /// </summary>
    Memory = 0,

    /// <summary>
    /// A distributed cache (Redis, SQL Server, …) shared by every application instance.
    /// The application registers the <c>IDistributedCache</c> implementation itself.
    /// </summary>
    Distributed = 1,
}
