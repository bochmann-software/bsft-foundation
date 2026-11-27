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
    private readonly ITranslationChain _chain;
    private readonly TranslationSeed _seed;
    private readonly IMemoryCache _cache;

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
    /// <para>
    /// The value is taken once, at construction: the factory is a singleton and the localizers it
    /// hands out are long-lived, so a later change to the section would reach some of them and not
    /// others. A locale setting that changed under a running application would also strand every
    /// cache entry resolved under the previous one.
    /// </para>
    /// <para>
    /// A factory built this way owns a chain of its own, which is not the one
    /// <see cref="ITranslationLookup"/> resolves through. <c>AddBsftLocalization</c> builds the factory
    /// over the registered chain instead, so that the localizers and the lookup share it.
    /// </para>
    /// </remarks>
    public BsftStringLocalizerFactory(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor requests,
        TranslationSeed seed,
        IMemoryCache cache,
        IOptions<BsftLocalizationOptions> localeOptions)
        // Kept for callers that construct the factory by hand: they have no registered chain, so the
        // factory builds its own. The registration goes through the internal constructor.
        : this(new TranslationChain(scopeFactory, requests, seed, cache, localeOptions), seed, cache)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftStringLocalizerFactory"/> class over the
    /// resolution chain the registered services share.
    /// </summary>
    /// <param name="chain">The chain every localizer this factory creates resolves through.</param>
    /// <param name="seed">The seed translations, from the singleton the chain was built over.</param>
    /// <param name="cache">The memory cache, from the singleton the chain was built over.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    internal BsftStringLocalizerFactory(ITranslationChain chain, TranslationSeed seed, IMemoryCache cache)
    {
        _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        _seed = seed ?? throw new ArgumentNullException(nameof(seed));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public IStringLocalizer Create(Type resourceSource) => NewLocalizer();

    /// <inheritdoc />
    public IStringLocalizer Create(string baseName, string location) => NewLocalizer();

    /// <summary>
    /// Creates the reader the bundle endpoints serve out of.
    /// </summary>
    /// <returns>A reader over the same chain, seeds and cache the localizers read.</returns>
    /// <remarks>
    /// The whole-bundle reads are on the localizer because they share the fallback chain with the
    /// per-key ones. Handing them out under a contract of their own is what keeps the endpoints from
    /// having to know which implementation of <see cref="IStringLocalizerFactory"/> is registered.
    /// </remarks>
    internal ITranslationBundleReader CreateBundleReader() => NewLocalizer();

    /// <summary>Builds a localizer over this factory's chain.</summary>
    /// <returns>The new localizer.</returns>
    private BsftStringLocalizer NewLocalizer()
        => new(_chain, _seed, _cache);
}
