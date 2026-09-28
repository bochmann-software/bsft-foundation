using Bsft.Caching.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Caching.Data.DependencyInjection;

/// <summary>
/// Extension methods for registering the tenant-aware cache key scope.
/// </summary>
public static class CachingDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TenantCacheKeyScopeProvider"/> as the <see cref="ICacheKeyScopeProvider"/>
    /// every cache key is qualified with, so a result cached for one tenant is never served to
    /// another. Requires multi-tenancy (<c>AddBsftDataMultiTenancy</c>) for the tenant context and the
    /// caching core (<c>AddBsftCaching</c>) for the key builder that reads the scope; the order of the
    /// three calls does not matter.
    /// </summary>
    /// <remarks>
    /// The global default <c>AddBsftCaching</c> registers is removed rather than merely tried past,
    /// because it is registered with <c>TryAdd</c> and would otherwise win whenever the host called
    /// <c>AddBsftCaching</c> first — the more common order, and the one whose failure is silent. A
    /// provider the host registered itself is left alone: this method adds with <c>TryAdd</c> too, so
    /// an application that folds tenant *and* user into its scope keeps its own. Calling this twice
    /// registers a single provider.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftCachingData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        RemoveGlobalScopeProvider(services);

        services.TryAddScoped<ICacheKeyScopeProvider, TenantCacheKeyScopeProvider>();

        return services;
    }

    /// <summary>Removes the global scope provider the caching core registers as its default.</summary>
    /// <param name="services">The service collection to clean up.</param>
    private static void RemoveGlobalScopeProvider(IServiceCollection services)
    {
        var defaults = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(ICacheKeyScopeProvider)
                && descriptor.ImplementationType == typeof(GlobalCacheKeyScopeProvider))
            .ToList();

        foreach (var descriptor in defaults)
            services.Remove(descriptor);
    }
}
