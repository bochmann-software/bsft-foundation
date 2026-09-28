using Bsft.Caching.Keys;
using Bsft.Data.MultiTenancy;

namespace Bsft.Caching.Data;

/// <summary>
/// An <see cref="ICacheKeyScopeProvider"/> that qualifies every cache key with the tenant the
/// current request resolved to, so a cacheable query answered for one tenant is never served to
/// another. While no tenant is resolved it reports <see cref="CacheKeyScope.NotCacheable"/>, so the
/// request bypasses the cache instead of sharing one.
/// </summary>
/// <remarks>
/// This is the provider the caching documentation tells a multi-tenant host to register, shipped
/// rather than left as an example: the default <c>GlobalCacheKeyScopeProvider</c> returns the global
/// scope, and a host that keeps it while resolving tenants gets cache entries shared across tenant
/// boundaries with nothing at runtime to signal it.
/// <para>
/// An unresolved tenant is not given a substitute discriminator. Every request that failed to
/// resolve would share it, and one of them may still produce a tenant-specific result — a host
/// authenticating through another mechanism, a dropped header, a caller who omitted it on purpose —
/// which the next unresolved request would then be served. Reporting the request uncacheable costs
/// the hit and stores nothing, so there is no bucket to share.
/// </para>
/// <para>
/// The provider is resolved per scope, because <see cref="ITenantContext"/> is: a singleton would
/// freeze the first request's tenant into every key it built afterwards. That is also why the
/// resolved scope may be memoized — within one scope the tenant may still arrive, but it cannot
/// change afterwards.
/// </para>
/// </remarks>
public sealed class TenantCacheKeyScopeProvider : ICacheKeyScopeProvider
{
    private readonly ITenantContext _tenantContext;

    /// <summary>The scope computed for the resolved tenant, kept once it exists.</summary>
    private string? _scope;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantCacheKeyScopeProvider"/> class.
    /// </summary>
    /// <param name="tenantContext">The context carrying the tenant the current request resolved to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantContext"/> is null.</exception>
    public TenantCacheKeyScopeProvider(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cacheable only while a tenant is resolved. The prefix plus identifier is composed once and
    /// reused for the rest of the scope; while unresolved the context is asked again on every call,
    /// because the tenant may still arrive.
    /// </remarks>
    public CacheKeyScope GetScope()
    {
        if (_scope is not null)
            return CacheKeyScope.For(_scope);

        // Blank counts as unresolved, and the check has to happen here rather than in the scope: the
        // prefix in front of the identifier makes the composed discriminator non-blank whatever the
        // identifier is, so "tenant:" would pass as a scope of its own and be shared by every request
        // whose tenant claim carried an empty value. The rest of the framework reads an empty tenant
        // id as absent for the same reason.
        if (_tenantContext is not { IsResolved: true, TenantId: { } tenantId }
            || string.IsNullOrWhiteSpace(tenantId))
        {
            return CacheKeyScope.NotCacheable;
        }

        _scope = CachingDataDefaults.TenantScopePrefix + tenantId;

        return CacheKeyScope.For(_scope);
    }
}
