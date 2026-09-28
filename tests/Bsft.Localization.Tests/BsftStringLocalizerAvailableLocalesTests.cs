using Bsft.Localization.EFCore;
using Bsft.Localization.Seeding;
using Bsft.Localization.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Localization.Tests;

public class BsftStringLocalizerAvailableLocalesTests : IDisposable
{
    private readonly TestTranslationDbContext _dbContext;
    private readonly MemoryCache _cache;

    public BsftStringLocalizerAvailableLocalesTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    private BsftStringLocalizer CreateLocalizer(
        TranslationSeed seed,
        string defaultLocale = "en",
        string fallbackLocale = "en")
    {
        var scopeFactory = TranslationReaderFactory.ScopeFactoryFor(_dbContext);

        var localeOptions = new BsftLocalizationOptions { DefaultLocale = defaultLocale, FallbackLocale = fallbackLocale };
        return new BsftStringLocalizer(scopeFactory, new HttpContextAccessor(), seed, _cache, localeOptions);
    }

    [Fact]
    public void GetAvailableLocales_EmptySeedAndDb_ReturnsEmptyList()
    {
        var localizer = CreateLocalizer(new TranslationSeed());

        var result = localizer.ListAvailableLocales();

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableLocales_SeedOnly_ReturnsSeedLocales()
    {
        var seed = new TranslationSeed();
        seed.Add("de", "greeting", "Hallo!");
        seed.Add("en", "greeting", "Hello!");

        var localizer = CreateLocalizer(seed);

        var result = localizer.ListAvailableLocales();

        result.Should().Equal("de", "en");
    }

    [Fact]
    public void GetAvailableLocales_DbOnly_ReturnsDbLocales()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "fr", Value = "Bonjour!", TenantId = null
        });
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello!", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed());

        var result = localizer.ListAvailableLocales();

        result.Should().Equal("en", "fr");
    }

    [Fact]
    public void GetAvailableLocales_MergesDbAndSeed_ReturnsDistinctSortedLocales()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "fr", Value = "Bonjour!", TenantId = null
        });
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello!", TenantId = null
        });
        _dbContext.SaveChanges();

        var seed = new TranslationSeed();
        seed.Add("de", "greeting", "Hallo!");
        seed.Add("en", "greeting", "Hi!");

        var localizer = CreateLocalizer(seed);

        var result = localizer.ListAvailableLocales();

        result.Should().Equal("de", "en", "fr");
    }

    [Fact]
    public void GetAvailableLocales_DuplicateLocalesAcrossDbAndSeed_AreDeduplicatedCaseInsensitively()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "EN", Value = "Hello!", TenantId = null
        });
        _dbContext.SaveChanges();

        var seed = new TranslationSeed();
        seed.Add("en", "greeting", "Hi!");

        var localizer = CreateLocalizer(seed);

        var result = localizer.ListAvailableLocales();

        result.Should().HaveCount(1);
    }

    [Fact]
    public void GetAvailableLocales_MultipleDbEntriesSameLocale_ReturnsDistinct()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello!", TenantId = null
        });
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "farewell", Locale = "en", Value = "Goodbye!", TenantId = null
        });
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "de", Value = "Hallo!", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed());

        var result = localizer.ListAvailableLocales();

        result.Should().Equal("de", "en");
    }

    [Fact]
    public void GetAvailableLocales_ResultIsSortedAlphabetically()
    {
        var seed = new TranslationSeed();
        seed.Add("zh", "key", "value");
        seed.Add("ar", "key", "value");
        seed.Add("fr", "key", "value");
        seed.Add("en", "key", "value");

        var localizer = CreateLocalizer(seed);

        var result = localizer.ListAvailableLocales();

        result.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        result.Should().Equal("ar", "en", "fr", "zh");
    }

    public void Dispose()
    {
        _cache.Dispose();
        _dbContext.Dispose();
    }
}
