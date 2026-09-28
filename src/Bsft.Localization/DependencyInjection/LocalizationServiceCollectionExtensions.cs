using Bsft.Localization.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Bsft.Localization.DependencyInjection;

/// <summary>
/// Extension methods for registering Bsft localization services with the dependency injection container.
/// </summary>
public static class LocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers translation seed data built from the provided configuration delegate.
    /// Seeds are applied at startup using an insert-if-not-exists strategy.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">A delegate that configures seed translations via <see cref="TranslationSeedBuilder"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// The seed stays on this package rather than on <c>Bsft.Localization.EFCore</c>: it is a fallback
    /// level of the resolution chain in its own right, consulted whether or not the application ever
    /// writes it to a store, and a host that seeds nothing still needs the singleton the localizer
    /// reads.
    /// </remarks>
    public static IServiceCollection AddBsftTranslations(
        this IServiceCollection services,
        Action<TranslationSeedBuilder> configure)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        var builder = new TranslationSeedBuilder();
        configure(builder);
        var seed = builder.Build();

        services.AddSingleton(seed);
        return services;
    }

    /// <summary>
    /// Registers the Bsft localization subsystem including the string localizer factory,
    /// memory cache, and <see cref="TranslationsUpdatedEvent"/> subscription for cache invalidation.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Configures the localization options, or <see langword="null"/> to take the defaults.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    /// <remarks>
    /// This registers the resolution chain and nothing that stores translations. Add
    /// <c>AddBsftLocalizationEFCore&lt;TContext&gt;</c> from <c>Bsft.Localization.EFCore</c>, or another
    /// implementation of <see cref="ITranslationReader"/>, for the store half.
    /// </remarks>
    public static IServiceCollection AddBsftLocalization(
        this IServiceCollection services,
        Action<BsftLocalizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Registers the Bsft localization subsystem with its options bound from configuration.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Localization</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IServiceCollection AddBsftLocalization(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftLocalizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(services, configuration, configure);
    }

    /// <summary>Binds, validates and registers, whichever overload was called.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">Adjusts what the section bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    /// <remarks>
    /// The options are validated inside this call as well as through the registered
    /// <see cref="IValidateOptions{TOptions}"/>, so a blank locale or a non-positive ceiling is a
    /// failure of the registration rather than of the first request that reads it.
    /// </remarks>
    private static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftLocalizationOptions>? configure)
    {
        void Apply(BsftLocalizationOptions target)
        {
            configuration?.GetSection(BsftLocalizationOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftLocalizationOptions();

        Apply(options);
        options.Validate();

        services.AddOptions<BsftLocalizationOptions>().Configure(Apply).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftLocalizationOptions>, BsftLocalizationOptionsValidator>());

        services.AddMemoryCache();

        // The localizer reads the tenant of the request it is serving, and the tenant context is
        // scoped state the tenant middleware writes onto that request's scope.
        services.AddHttpContextAccessor();

        // Registered by its own type first, so that both the localizer factory and the bundle
        // reader are the same instance and a host replacing IStringLocalizerFactory with one of its
        // own still leaves the bundle endpoints something to read.
        services.TryAddSingleton<BsftStringLocalizerFactory>();
        services.AddSingleton<IStringLocalizerFactory>(sp => sp.GetRequiredService<BsftStringLocalizerFactory>());
        services.TryAddSingleton<ITranslationBundleReader>(
            sp => sp.GetRequiredService<BsftStringLocalizerFactory>().CreateBundleReader());

        // Subscribe to TranslationsUpdatedEvent to invalidate cache
        services.AddSingleton<TranslationCacheInvalidator>();
        services.AddHostedService<TranslationCacheInvalidatorHostedService>();

        return services;
    }
}
