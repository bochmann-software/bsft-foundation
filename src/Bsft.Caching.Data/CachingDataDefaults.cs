namespace Bsft.Caching.Data;

/// <summary>
/// The constants the tenant-aware cache key scope is composed of, published so a host that has to
/// reproduce a key outside the framework — a diagnostic tool reading the distributed cache, a test
/// asserting on an entry — spells the scope the same way the provider does.
/// </summary>
public static class CachingDataDefaults
{
    /// <summary>
    /// The prefix a resolved tenant's scope carries, followed by the tenant identifier.
    /// </summary>
    /// <remarks>
    /// There is no counterpart for an unresolved tenant: such a request is reported uncacheable and
    /// gets no key at all, rather than a substitute scope every unresolved request would share.
    /// </remarks>
    public const string TenantScopePrefix = "tenant:";
}
