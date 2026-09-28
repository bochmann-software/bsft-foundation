using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Bsft.Localization;

/// <summary>
/// Creates <see cref="BsftStringLocalizer"/> instances that resolve translations
/// from the translation store, seeds, and fallback locales. Registered as a singleton;
/// scoped dependencies (the reader, the tenant context) are resolved per-request
/// via <see cref="IServiceScopeFactory"/>.
/// </summary>
public sealed class BsftStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _requests;
    private readonly TranslationSeed _seed;
    private readonly IMemoryCache _cache;
    private readonly BsftLocalizationOptions _localeOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftStringLocalizerFactory"/> class.
    /// </summary>
    /// <param name="scopeFactory">The scope factory for resolving scoped services outside a request.</param>
    /// <param name="requests">The ambient request, for the lookups that happen inside one.</param>
    /// <param name="seed">The seed translations.</param>
    /// <param name="cache">The memory cache used for translation caching.</param>
    /// <param name="localeOptions">The localization options containing locale settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <remarks>
    /// The value is taken once, at construction: the factory is a singleton and the localizers it
    /// hands out are long-lived, so a later change to the section would reach some of them and not
    /// others. A locale setting that changed under a running application would also strand every
    /// cache entry resolved under the previous one.
    /// </remarks>
    public BsftStringLocalizerFactory(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor requests,
        TranslationSeed seed,
        IMemoryCache cache,
        IOptions<BsftLocalizationOptions> localeOptions)
    {
        ArgumentNullException.ThrowIfNull(localeOptions);

        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _seed = seed ?? throw new ArgumentNullException(nameof(seed));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _localeOptions = localeOptions.Value ?? throw new ArgumentNullException(nameof(localeOptions));
    }

    /// <inheritdoc />
    public IStringLocalizer Create(Type resourceSource) => NewLocalizer();

    /// <inheritdoc />
    public IStringLocalizer Create(string baseName, string location) => NewLocalizer();

    /// <summary>
    /// Creates the reader the bundle endpoints serve out of.
    /// </summary>
    /// <returns>A reader over the same store, seeds and cache the localizers read.</returns>
    /// <remarks>
    /// The whole-bundle reads are on the localizer because they share the fallback chain with the
    /// per-key ones. Handing them out under a contract of their own is what keeps the endpoints from
    /// having to know which implementation of <see cref="IStringLocalizerFactory"/> is registered.
    /// </remarks>
    internal ITranslationBundleReader CreateBundleReader() => NewLocalizer();

    /// <summary>Builds a localizer over this factory's dependencies.</summary>
    /// <returns>The new localizer.</returns>
    private BsftStringLocalizer NewLocalizer()
        => new(_scopeFactory, _requests, _seed, _cache, _localeOptions);
}
