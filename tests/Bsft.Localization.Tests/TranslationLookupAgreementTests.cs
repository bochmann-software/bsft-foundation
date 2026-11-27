using System.Globalization;
using Bsft.Localization.Tests.Fakes;
using Bsft.Localization.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Bsft.Localization.Tests;

/// <summary>
/// The string localizer and the culture-explicit lookup resolve through one chain, so for the same
/// tenant, locale and key they give the same answer, whichever level of the chain holds it.
/// </summary>
public class TranslationLookupAgreementTests : IDisposable
{
    private readonly MemoryCache _localizerCache = new(new MemoryCacheOptions());
    private readonly MemoryCache _lookupCache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _localizerCache.Dispose();
        _lookupCache.Dispose();
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Find_AndTheLocalizer_AgreeForTheSameTenantLocaleAndKey(Scenario scenario)
    {
        var reader = new FakeTranslationReader(scenario.StoreRows);
        var seed = scenario.CreateSeed();
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };
        var scopeFactory = TranslationReaderFactory.ScopeFactoryFor(reader, scenario.Tenant);

        var localizer = new BsftStringLocalizer(
            scopeFactory, new HttpContextAccessor(), seed, _localizerCache, options);

        var chain = new TranslationChain(scopeFactory, new HttpContextAccessor(), seed, _lookupCache, options);
        var lookup = new TranslationLookup(
            chain, new TranslationMissCache(Options.Create(options), new FakeTimeProvider()), Options.Create(options));

        var fromLocalizer = localizer.Resolve(scenario.Key, scenario.Locale);
        var fromLookup = lookup.Find(scenario.Tenant, new CultureInfo(scenario.Locale), scenario.Key);

        fromLookup.Should().Be(fromLocalizer);
        fromLookup.Should().Be(scenario.Expected);
    }

    [Fact]
    public void Constructor_OverASharedChain_ResolvesAndCachesWhatFindReturns()
    {
        var reader = new FakeTranslationReader(new Translation("greeting", "en", "Hello", "t1"));
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };
        var seed = new TranslationSeed();
        var chain = new TranslationChain(
            TranslationReaderFactory.ScopeFactoryFor(reader, "t1"),
            new HttpContextAccessor(),
            seed,
            _lookupCache,
            options);
        var localizer = new BsftStringLocalizer(chain, seed, _lookupCache);
        var lookup = new TranslationLookup(
            chain, new TranslationMissCache(Options.Create(options), new FakeTimeProvider()), Options.Create(options));

        localizer.Resolve("greeting", "en").Should().Be("Hello");

        lookup.Find("t1", new CultureInfo("en"), "greeting").Should().Be("Hello");
        reader.KeyReads.Should().Be(1);
    }

    [Fact]
    public void Resolve_KeyMissed_IsNotRememberedByTheLocalizer()
    {
        var reader = new FakeTranslationReader();
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };
        var localizer = new BsftStringLocalizer(
            TranslationReaderFactory.ScopeFactoryFor(reader),
            new HttpContextAccessor(),
            new TranslationSeed(),
            _localizerCache,
            options);

        localizer.Resolve("greeting", "en").Should().BeNull();
        localizer.Resolve("greeting", "en").Should().BeNull();

        reader.KeyReads.Should().Be(2);
    }

    [Fact]
    public void Resolve_KeyAddedAfterAMiss_IsVisibleAtOnce()
    {
        var reader = new FakeTranslationReader();
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };
        var localizer = new BsftStringLocalizer(
            TranslationReaderFactory.ScopeFactoryFor(reader),
            new HttpContextAccessor(),
            new TranslationSeed(),
            _localizerCache,
            options);

        localizer.Resolve("greeting", "en").Should().BeNull();
        reader.Add(new Translation("greeting", "en", "Hello", null));

        localizer.Resolve("greeting", "en").Should().Be("Hello");
    }

    public static TheoryData<Scenario> Scenarios => new()
    {
        new Scenario(
            "a tenant row beats the global row",
            "t1", "en", "greeting",
            Rows: [new Translation("greeting", "en", "Tenant", "t1"), new Translation("greeting", "en", "Global", null)],
            Expected: "Tenant"),
        new Scenario(
            "a global row answers when the tenant has none",
            "t1", "en", "greeting",
            Rows: [new Translation("greeting", "en", "Global", null)],
            Expected: "Global"),
        new Scenario(
            "a row of another tenant is never used",
            "t1", "en", "greeting",
            Rows: [new Translation("greeting", "en", "Other", "t2")],
            Expected: null),
        new Scenario(
            "the seed answers when no row does",
            null, "en", "greeting",
            Seed: [("en", "greeting", "Seed")],
            Expected: "Seed"),
        new Scenario(
            "the fallback locale's row answers for a locale without one",
            null, "de", "greeting",
            Rows: [new Translation("greeting", "en", "Fallback row", null)],
            Expected: "Fallback row"),
        new Scenario(
            "the fallback locale's seed answers for a locale without one",
            null, "de", "greeting",
            Seed: [("en", "greeting", "Fallback seed")],
            Expected: "Fallback seed"),
        new Scenario(
            "a key that no level has resolves to null in both",
            "t1", "de", "missing",
            Rows: [new Translation("greeting", "en", "Hello", null)],
            Expected: null),
    };

    public sealed record Scenario(
        string Name,
        string? Tenant,
        string Locale,
        string Key,
        Translation[]? Rows = null,
        (string Locale, string Key, string Value)[]? Seed = null,
        string? Expected = null)
    {
        public Translation[] StoreRows => Rows ?? [];

        public TranslationSeed CreateSeed()
        {
            var seed = new TranslationSeed();
            foreach (var (locale, key, value) in Seed ?? [])
                seed.Add(locale, key, value);

            return seed;
        }

        public override string ToString() => Name;
    }
}
