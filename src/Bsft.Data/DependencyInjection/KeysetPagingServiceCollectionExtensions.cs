using Bsft.Data.KeysetPaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Data.DependencyInjection;

/// <summary>Extension methods for registering keyset (cursor) paging.</summary>
public static class KeysetPagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IKeysetPageReader"/> and <see cref="IKeysetCursorSerializer"/>, both stateless
    /// singletons, and the bounds in <see cref="BsftDataOptions.KeysetPaging"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Adjusts the bounds, such as the largest page.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Separate from <see cref="DataServiceCollectionExtensions.AddBsftDataQueryFiltering"/>, because a
    /// keyset listing has a fixed order and declares no filter surface; a host that has both calls both.
    /// Registered with <c>TryAdd</c>, so a host may replace the serializer first. The bounds are validated
    /// at start-up.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftDataKeysetPaging(
        this IServiceCollection services, Action<KeysetPagingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Register(services, configuration: null, configure);
    }

    /// <summary>
    /// Registers the same services, with the bounds bound from <c>Bsft:Data:KeysetPaging</c> first.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Data</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IServiceCollection AddBsftDataKeysetPaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<KeysetPagingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Register(services, configuration, configure);
    }

    private static IServiceCollection Register(
        IServiceCollection services, IConfiguration? configuration, Action<KeysetPagingOptions>? configure)
    {
        services.AddOptions<BsftDataOptions>()
            .Configure(options =>
            {
                configuration?.GetSection(BsftDataOptions.SectionName).GetSection(nameof(BsftDataOptions.KeysetPaging)).Bind(options.KeysetPaging);
                configure?.Invoke(options.KeysetPaging);
            })
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftDataOptions>, BsftDataOptionsValidator>());
        services.TryAddSingleton<IKeysetCursorSerializer, KeysetCursorSerializer>();
        services.TryAddSingleton<IKeysetQueryComposer, KeysetQueryComposer>();
        services.TryAddSingleton<IKeysetPageReader, KeysetPageReader>();
        return services;
    }
}
