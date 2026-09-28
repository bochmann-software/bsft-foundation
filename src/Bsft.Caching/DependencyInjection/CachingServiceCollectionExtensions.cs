using Bsft.Caching.Invalidation;
using Bsft.Caching.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Caching.DependencyInjection;

/// <summary>
/// Extension methods for registering the Bsft caching subsystem.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the cache store selected by <see cref="BsftCachingOptions.Provider"/>, the tag
    /// index, the key builder and the invalidator. Calling it more than once registers each once.
    /// </summary>
    /// <remarks>
    /// Caching a mediator query is the job of <c>Bsft.Caching.Mediator</c>, whose
    /// <c>AddBsftCachingMediator</c> adds the pipeline behavior over what this method registers.
    /// With <see cref="CacheProvider.Distributed"/> the application must register its own
    /// <c>IDistributedCache</c> implementation.
    /// <para>
    /// The tag index is registered as an in-process, volatile singleton for either provider. With
    /// <see cref="CacheProvider.Distributed"/> the store is shared and survives a restart while the
    /// index does not, so <see cref="ICacheInvalidator.InvalidateTagAsync"/> evicts only what this
    /// instance cached since it started. An application that needs tag invalidation to reach the
    /// whole fleet registers its own <see cref="ICacheTagIndex"/> over shared storage before calling
    /// this method: every service here is registered with <c>TryAdd</c>, so an existing registration
    /// wins.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configure">An optional action to override the default caching options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftCaching(
        this IServiceCollection services,
        Action<BsftCachingOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        // Applied once, here, rather than from inside the registered Configure delegate. Which store
        // type is registered is a container decision that cannot wait for IOptions, so the provider has
        // to be read at registration time; running the caller's delegate a second time later would let
        // a delegate that is not purely deterministic hand the container one provider and IOptions
        // another, and the store silently would not be the one the rest of the application believes in.
        var configured = new BsftCachingOptions();
        configure?.Invoke(configured);

        services.AddOptions<BsftCachingOptions>()
            .Configure(options => CopyFrom(configured, options))
            .ValidateOnStart();

        return AddCore(services, configured.Provider);
    }

    /// <summary>
    /// Registers the caching subsystem with its options bound from configuration section
    /// <see cref="BsftCachingOptions.SectionName"/>.
    /// </summary>
    /// <remarks>
    /// Everything the <see cref="Action{T}"/> overload says applies here too.
    /// </remarks>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configuration">The configuration to bind <c>Bsft:Caching</c> from.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static IServiceCollection AddBsftCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        // Bound once, for the same reason the Action overload applies its delegate once: the provider
        // decides which store is registered, and a section that answers differently on a second read —
        // reloadOnChange is the ordinary case — would leave the container and IOptions disagreeing.
        var configured = new BsftCachingOptions();
        configuration.GetSection(BsftCachingOptions.SectionName).Bind(configured);

        services.AddOptions<BsftCachingOptions>()
            .Configure(options => CopyFrom(configured, options))
            .ValidateOnStart();

        return AddCore(services, configured.Provider);
    }

    /// <summary>Copies the settings resolved at registration time onto the options the container owns.</summary>
    /// <remarks>
    /// Written out member by member rather than reflected over, so that a member added to the options
    /// without a line here fails review rather than silently stopping at its default.
    /// </remarks>
    /// <param name="source">The options as the caller configured or bound them.</param>
    /// <param name="target">The options instance the options infrastructure hands out.</param>
    private static void CopyFrom(BsftCachingOptions source, BsftCachingOptions target)
    {
        target.Provider = source.Provider;
        target.DefaultTimeToLive = source.DefaultTimeToLive;
        target.KeyPrefix = source.KeyPrefix;
    }

    /// <summary>Registers everything that does not depend on which store was selected.</summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="provider">The selected cache provider.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddCore(IServiceCollection services, CacheProvider provider)
    {
        // The stores report a cache they could not reach rather than failing the caller, so the
        // module needs a logger to be resolvable. AddLogging is idempotent and leaves an
        // application's own configuration alone.
        services.AddLogging();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftCachingOptions>, BsftCachingOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        // The scope provider reads per-request ambient state, so it and the key builder that
        // captures it are scoped: a singleton builder would freeze the first request's scope into
        // every key, or make an application's scoped provider a captive dependency.
        services.TryAddScoped<ICacheKeyScopeProvider, GlobalCacheKeyScopeProvider>();
        services.TryAddScoped<ICacheKeyBuilder, CacheKeyBuilder>();
        // In-process and volatile for either provider, which bounds tag invalidation to what this
        // instance cached since it started. TryAdd makes it the seam an application replaces with a
        // shared-storage index when it needs tag invalidation to reach the whole fleet.
        services.TryAddSingleton<ICacheTagIndex, CacheTagIndex>();
        services.TryAddSingleton<ICacheSerializer, JsonCacheSerializer>();
        services.TryAddSingleton<ICacheInvalidator, CacheInvalidator>();

        RegisterStore(services, provider);

        return services;
    }

    private static void RegisterStore(IServiceCollection services, CacheProvider provider)
    {
        switch (provider)
        {
            case CacheProvider.Memory:
                services.AddMemoryCache();
                services.TryAddSingleton<ICacheStore, MemoryCacheStore>();
                break;

            case CacheProvider.Distributed:
                services.TryAddSingleton<ICacheStore, DistributedCacheStore>();
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(provider),
                    provider,
                    "Unknown cache provider.");
        }
    }
}
