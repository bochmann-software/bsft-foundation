using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Warns at startup when a multi-tenant host caches with the global, tenant-blind cache key scope.
/// </summary>
/// <remarks>
/// <para>
/// <c>Bsft.Caching</c> qualifies every key with the scope its <c>ICacheKeyScopeProvider</c> returns,
/// and the default provider returns none. In a host that resolves tenants, a cacheable query whose
/// result differs per tenant then shares one entry between them: two callers who each passed
/// authorization, one answer. The failure is silent and it is cross-tenant, which is why it is worth
/// a startup message rather than a paragraph in the documentation.
/// </para>
/// <para>
/// It is a warning and not a failure because caching globally is a legitimate configuration — a host
/// may cache only tenant-independent lookups, or fold the tenant into every <c>CacheKey</c> by hand —
/// and this validator cannot tell those apart from the mistake. What it can do is make the mistake
/// loud.
/// </para>
/// <para>
/// The check reads the service collection the host built rather than the built provider, for the same
/// reason <see cref="PooledTenantContextStartupValidator"/> does: which implementation backs a seam is
/// visible as a registration, and reading the collection makes the order of the registration calls
/// irrelevant. The two caching types are matched by name, because this package must not reference
/// <c>Bsft.Caching</c> — that would put a cache in front of every consumer of the data layer. If the
/// caching package renames them, this validator stops reporting rather than reporting wrongly.
/// </para>
/// </remarks>
public sealed class TenantCacheScopeStartupValidator : IStartupValidator
{
    /// <summary>The full name of the caching seam supplying the scope every key is qualified with.</summary>
    private const string ScopeProviderTypeName = "Bsft.Caching.Keys.ICacheKeyScopeProvider";

    /// <summary>The full name of the caching default that qualifies keys with no scope at all.</summary>
    private const string GlobalScopeProviderTypeName = "Bsft.Caching.Keys.GlobalCacheKeyScopeProvider";

    /// <summary>The finding an operator reads when caching stays on the tenant-blind default.</summary>
    private const string GlobalScopeWarning =
        "Multi-tenancy is configured and caching is registered with the global ICacheKeyScopeProvider, which qualifies "
        + "cache keys with no tenant at all: a cacheable query whose result differs per tenant serves one tenant's answer "
        + "to the next. Register the tenant-aware TenantCacheKeyScopeProvider by referencing Bsft.Caching.Data and calling "
        + "AddBsftCachingData(), or register a scope provider of your own.";

    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantCacheScopeStartupValidator"/> class.
    /// </summary>
    /// <param name="services">The service collection the host is registering into.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public TenantCacheScopeStartupValidator(IServiceCollection services) =>
        _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    /// <remarks>
    /// The finding travels as a warning on the result rather than as a log statement of this class's
    /// own, so the host runner stays the single place a startup finding is reported.
    /// </remarks>
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCachingWithTheGlobalScope())
            return Task.FromResult(StartupValidationResult.Success("The cache key scope is not the tenant-blind default."));

        return Task.FromResult(
            StartupValidationResult.Success(
                "Caching runs on the global cache key scope.",
                [GlobalScopeWarning]));
    }

    /// <summary>
    /// Whether caching is registered and the scope provider that wins is the tenant-blind default.
    /// </summary>
    /// <returns><see langword="true"/> when it is.</returns>
    /// <remarks>
    /// The last registration is the one the container resolves, so only that one is examined: a host
    /// that registered its own provider after <c>AddBsftCaching</c> is not warned about the default it
    /// replaced.
    /// </remarks>
    private bool IsCachingWithTheGlobalScope()
    {
        var effective = _services.LastOrDefault(descriptor => IsScopeProvider(descriptor.ServiceType));

        return effective is not null
            && string.Equals(
                effective.ImplementationType?.FullName,
                GlobalScopeProviderTypeName,
                StringComparison.Ordinal);
    }

    /// <summary>Whether a service type is the caching scope seam.</summary>
    /// <param name="serviceType">The registered service type.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsScopeProvider(Type serviceType) =>
        string.Equals(serviceType.FullName, ScopeProviderTypeName, StringComparison.Ordinal);
}
