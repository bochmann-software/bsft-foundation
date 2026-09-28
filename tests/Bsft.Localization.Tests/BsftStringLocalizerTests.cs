using Bsft.Localization.EFCore;
using Bsft.Localization.Tests.Fakes;
using Bsft.Localization.Seeding;
using Bsft.Localization.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Xunit;

namespace Bsft.Localization.Tests;

public class BsftStringLocalizerTests : IDisposable
{
    private readonly TestTranslationDbContext _dbContext;
    private readonly MemoryCache _cache;

    public BsftStringLocalizerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    private BsftStringLocalizer CreateLocalizer(
        TranslationSeed seed,
        string defaultLocale = "en",
        string fallbackLocale = "en",
        string? tenantId = null)
    {
        var scopeFactory = TranslationReaderFactory.ScopeFactoryFor(_dbContext, tenantId);

        var localeOptions = new BsftLocalizationOptions { DefaultLocale = defaultLocale, FallbackLocale = fallbackLocale };
        return new BsftStringLocalizer(scopeFactory, new HttpContextAccessor(), seed, _cache, localeOptions);
    }

    [Fact]
    public void Resolve_TenantDbEntry_ReturnsTenantValue()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Tenant!", TenantId = "t1"
        });
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Global!", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed(), tenantId: "t1");

        var result = localizer.Resolve("greeting", "en");

        result.Should().Be("Hello Tenant!");
    }

    [Fact]
    public void Resolve_NoTenantEntry_FallsBackToGlobalDb()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Global!", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed(), tenantId: "t1");

        var result = localizer.Resolve("greeting", "en");

        result.Should().Be("Hello Global!");
    }

    [Fact]
    public void Resolve_NoDbEntry_FallsBackToSeed()
    {
        var seed = new TranslationSeed();
        seed.Add("en", "greeting", "Hello Seed!");

        var localizer = CreateLocalizer(seed);

        var result = localizer.Resolve("greeting", "en");

        result.Should().Be("Hello Seed!");
    }

    [Fact]
    public void Resolve_NoMatchInPrimaryLocale_FallsBackToFallbackLocale()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Fallback!", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed(), defaultLocale: "de", fallbackLocale: "en");

        var result = localizer.Resolve("greeting", "de");

        result.Should().Be("Hello Fallback!");
    }

    [Fact]
    public void Resolve_NoMatchAnywhere_ReturnsNull()
    {
        var localizer = CreateLocalizer(new TranslationSeed());

        var result = localizer.Resolve("nonexistent", "en");

        result.Should().BeNull();
    }

    [Fact]
    public void Indexer_NoMatchAnywhere_ReturnsKeyAsValue()
    {
        var localizer = CreateLocalizer(new TranslationSeed());

        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var localized = localizer["nonexistent.key"];

            localized.Value.Should().Be("nonexistent.key");
            localized.ResourceNotFound.Should().BeTrue();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void Resolve_FallbackLocale_SeedFallback()
    {
        var seed = new TranslationSeed();
        seed.Add("en", "greeting", "Hello English Seed!");

        var localizer = CreateLocalizer(seed, defaultLocale: "de", fallbackLocale: "en");

        var result = localizer.Resolve("greeting", "de");

        result.Should().Be("Hello English Seed!");
    }

    [Fact]
    public void Resolve_CachesResult()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "cached", Locale = "en", Value = "CachedValue", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed());

        var first = localizer.Resolve("cached", "en");
        first.Should().Be("CachedValue");

        // Remove from DB; cached value should still be returned
        var entity = _dbContext.Translations.First(t => t.Key == "cached");
        _dbContext.Translations.Remove(entity);
        _dbContext.SaveChanges();

        var second = localizer.Resolve("cached", "en");
        second.Should().Be("CachedValue");
    }

    [Fact]
    public void Indexer_WithArguments_FormatsValue()
    {
        // The formatting indexer.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "hello", Locale = "en", Value = "Hello, {0}!", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed());

        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                new System.Globalization.CultureInfo("en");

            var result = localizer["hello", "World"];

            result.Value.Should().Be("Hello, World!");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void Indexer_WithArguments_MissingKey_ReturnsKeyName()
    {
        // The formatting indexer with nothing resolved: the key stands in for the value.
        var localizer = CreateLocalizer(new TranslationSeed());

        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                new System.Globalization.CultureInfo("en");

            var result = localizer["missing.key", "arg1"];

            result.Name.Should().Be("missing.key");
            result.ResourceNotFound.Should().BeTrue();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void Resolve_WithEmptyLocale_UsesDefaultLocale()
    {
        // An empty locale falls back to the configured default rather than resolving nothing.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "empty-locale-key", Locale = "en", Value = "Default locale value", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed(), defaultLocale: "en");

        // An empty locale, which the default stands in for
        var result = localizer.Resolve("empty-locale-key", "");

        result.Should().Be("Default locale value");
    }

    [Fact]
    public void GetAllStrings_WithDbTranslation_YieldsLocalizedStrings()
    {
        // With no tenant context the tenant is null, and the global rows of the culture are
        // what GetAllStrings yields.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello from DB", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed());

        // GetAllStrings uses CultureInfo.CurrentUICulture.Name
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                new System.Globalization.CultureInfo("en");

            var strings = localizer.GetAllStrings(false).ToList();

            strings.Should().Contain(s => s.Name == "greeting" && s.Value == "Hello from DB");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void GetAllStrings_WithTenantContext_UsesResolvedTenantId()
    {
        // With a resolved tenant context, the tenant's own rows are what GetAllStrings yields.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Tenant Hello", TenantId = "tenant-1"
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed(), tenantId: "tenant-1");

        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                new System.Globalization.CultureInfo("en");

            var strings = localizer.GetAllStrings(false).ToList();

            strings.Should().Contain(s => s.Name == "greeting" && s.Value == "Tenant Hello");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void ResolveBundle_WithDbEntries_ReturnsAllTranslations()
    {
        // The database rows of the locale are part of the resolved bundle.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "db-key", Locale = "en", Value = "DB Value", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed(), defaultLocale: "en", fallbackLocale: "en");

        var result = localizer.ResolveBundle("en").Translations;

        result.Should().ContainKey("db-key");
        result["db-key"].Should().Be("DB Value");
    }

    [Fact]
    public void ResolveBundle_WithFallbackLocaleSeeds_IncludesFallbackKeys()
    {
        // A locale that differs from the fallback also collects the fallback locale's seed keys.
        var seed = new TranslationSeed();
        seed.Add("en", "en-only-key", "English value");

        var localizer = CreateLocalizer(seed, defaultLocale: "de", fallbackLocale: "en");

        // Resolving "de" with fallback "en" has to surface the English seed key.
        var result = localizer.ResolveBundle("de").Translations;

        // The key from the English seed should be present via fallback
        result.Should().ContainKey("en-only-key");
    }

    [Fact]
    public void Indexer_WithArguments_MalformedValue_ReturnsRawValueInsteadOfThrowing()
    {
        // A stored value with an invalid format placeholder must not throw FormatException.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "bad", Locale = "en", Value = "Use braces: {not a placeholder}", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed());

        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");

            var result = localizer["bad", "World"];

            result.Value.Should().Be("Use braces: {not a placeholder}");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void ResolveBundle_WithTenantContext_IncludesTenantValue()
    {
        // Exercises the tenant branch of the in-memory bundle resolution.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Tenant!", TenantId = "t1"
        });
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Global!", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed(), defaultLocale: "en", fallbackLocale: "en", tenantId: "t1");

        var result = localizer.ResolveBundle("en").Translations;

        result.Should().ContainKey("greeting");
        result["greeting"].Should().Be("Hello Tenant!");
    }

    [Fact]
    public void Indexer_WithANullArgumentArray_ReturnsTheValueInsteadOfThrowing()
    {
        // A params parameter can be handed a null array explicitly. A caller who did that passed no
        // arguments, and a lookup that found its value must not throw over how it was called.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello!", TenantId = null
        });
        _dbContext.SaveChanges();

        var localizer = CreateLocalizer(new TranslationSeed());

        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");

            var result = localizer["greeting", null!];

            result.Value.Should().Be("Hello!");
            result.ResourceNotFound.Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("greeting", "EN")]
    [InlineData("Greeting", "en")]
    public void Resolve_ARowTheStoreMatchedUnderAnotherSpelling_IsStillResolved(string key, string locale)
    {
        // The store decides how strictly it matched, and under a case-insensitive collation it
        // answers with rows spelled differently from the request. The chain must not then be
        // stricter than the store it is filtering: the bundle already folds "EN" and "en" into one
        // locale, so a row would otherwise be listed as available and resolve to nothing.
        var reader = new StaticTranslationReader(new Translation(key, locale, "Hello!", TenantId: null));
        var localizer = CreateLocalizerOver(reader);

        localizer.Resolve("greeting", "en").Should().Be("Hello!");
    }

    private BsftStringLocalizer CreateLocalizerOver(ITranslationReader reader, string? tenantId = null)
        => new(
            TranslationReaderFactory.ScopeFactoryFor(reader, tenantId),
            new HttpContextAccessor(),
            new TranslationSeed(),
            _cache,
            new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" });

    [Fact]
    public void Resolve_ForATenantNamedLikeTheAbsentOne_DoesNotReadTheGlobalEntry()
    {
        // The cache key is composed rather than joined, so no tenant id can be spelled to collide
        // with the entry of a request that ran in no tenant at all.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Global!", TenantId = null
        });
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Global Inc!", TenantId = "global"
        });
        _dbContext.SaveChanges();

        CreateLocalizer(new TranslationSeed()).Resolve("greeting", "en").Should().Be("Hello Global!");

        var tenant = CreateLocalizer(new TranslationSeed(), tenantId: "global");

        tenant.Resolve("greeting", "en").Should().Be("Hello Global Inc!");
    }

    [Fact]
    public void ResolveBundle_ForATenantNamedLikeTheAbsentOne_DoesNotReadTheGlobalBundle()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Global!", TenantId = null
        });
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hello Global Inc!", TenantId = "global"
        });
        _dbContext.SaveChanges();

        var global = CreateLocalizer(new TranslationSeed()).ResolveBundle("en");
        var tenant = CreateLocalizer(new TranslationSeed(), tenantId: "global").ResolveBundle("en");

        global.Translations["greeting"].Should().Be("Hello Global!");
        tenant.Translations["greeting"].Should().Be("Hello Global Inc!");
        tenant.Version.Should().NotBe(global.Version);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _dbContext.Dispose();
    }
}
