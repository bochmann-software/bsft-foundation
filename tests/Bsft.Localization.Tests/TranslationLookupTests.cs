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
/// What <c>ITranslationLookup.Find</c> answers for the tenant and culture it is given, what it reads
/// from the store, and what it keeps for the next call.
/// </summary>
public class TranslationLookupTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void Find_IgnoresTheCurrentUICulture_AndAnswersForTheCultureGiven()
    {
        var reader = new FakeTranslationReader(new Translation("greeting", "en", "Hello", null));
        var lookup = CreateLookup(reader);

        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("de");

            var value = lookup.Find(null, new CultureInfo("en"), "greeting");

            value.Should().Be("Hello");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void Find_InvariantCulture_ResolvesUnderTheDefaultLocale()
    {
        var reader = new FakeTranslationReader(
            new Translation("greeting", "de", "Hallo", null),
            new Translation("greeting", "en", "Hello", null));
        var lookup = CreateLookup(reader, defaultLocale: "de");

        var value = lookup.Find(null, CultureInfo.InvariantCulture, "greeting");

        value.Should().Be("Hallo");
    }

    [Fact]
    public void Find_NullCulture_ThrowsArgumentNullException()
    {
        var lookup = CreateLookup(new FakeTranslationReader());

        var act = () => lookup.Find(null, null!, "greeting");

        act.Should().Throw<ArgumentNullException>().WithParameterName("culture");
    }

    [Fact]
    public void Find_NullKey_ThrowsArgumentException()
    {
        var lookup = CreateLookup(new FakeTranslationReader());

        var act = () => lookup.Find(null, new CultureInfo("en"), null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Find_EmptyKey_ThrowsArgumentException()
    {
        var lookup = CreateLookup(new FakeTranslationReader());

        var act = () => lookup.Find(null, new CultureInfo("en"), string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Find_SameKeyUnderTenantAOrBOrNone_ReturnsEachScopesOwnValue()
    {
        var reader = new FakeTranslationReader(
            new Translation("greeting", "en", "Hello A", "a"),
            new Translation("greeting", "en", "Hello B", "b"),
            new Translation("greeting", "en", "Hello global", null));
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find("a", english, "greeting").Should().Be("Hello A");
        lookup.Find("b", english, "greeting").Should().Be("Hello B");
        lookup.Find(null, english, "greeting").Should().Be("Hello global");
    }

    [Fact]
    public void Find_MissUnderOneTenant_DoesNotSuppressTheSameKeyUnderAnother()
    {
        var reader = new FakeTranslationReader(new Translation("greeting", "en", "Hello B", "b"));
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find("a", english, "greeting").Should().BeNull();
        var valueForB = lookup.Find("b", english, "greeting");

        valueForB.Should().Be("Hello B");
        reader.KeyReads.Should().Be(2);
    }

    [Fact]
    public void Find_Hit_IsWrittenToTheSharedMemoryCache()
    {
        var reader = new FakeTranslationReader(new Translation("greeting", "en", "Hello", "t1"));
        var lookup = CreateLookup(reader);

        lookup.Find("t1", new CultureInfo("en"), "greeting");

        _cache.TryGetValue(new TranslationCacheKey("t1", "en", "greeting"), out string? cached).Should().BeTrue();
        cached.Should().Be("Hello");
    }

    [Fact]
    public void Find_Miss_IsNotWrittenToTheSharedMemoryCache()
    {
        var reader = new FakeTranslationReader();
        var lookup = CreateLookup(reader);

        var value = lookup.Find("t1", new CultureInfo("en"), "missing");

        value.Should().BeNull();
        _cache.TryGetValue(new TranslationCacheKey("t1", "en", "missing"), out _).Should().BeFalse();
    }

    [Fact]
    public void Find_HitAlreadyCachedByTheLocalizer_ReturnsItWithoutAStoreRead()
    {
        var reader = new FakeTranslationReader(new Translation("greeting", "en", "Hello", "t1"));
        var localizer = new BsftStringLocalizer(
            TranslationReaderFactory.ScopeFactoryFor(reader, "t1"),
            new HttpContextAccessor(),
            new TranslationSeed(),
            _cache,
            new BsftLocalizationOptions());
        var lookup = CreateLookup(reader);

        localizer.Resolve("greeting", "en").Should().Be("Hello");
        var readsBefore = reader.KeyReads;

        var value = lookup.Find("t1", new CultureInfo("en"), "greeting");

        value.Should().Be("Hello");
        reader.KeyReads.Should().Be(readsBefore);
    }

    [Fact]
    public void Find_KeyMissedThenResolvedThroughTheLocalizer_AnswersWithTheValueNotTheRememberedMiss()
    {
        var reader = new FakeTranslationReader();
        var localizer = new BsftStringLocalizer(
            TranslationReaderFactory.ScopeFactoryFor(reader, "t1"),
            new HttpContextAccessor(),
            new TranslationSeed(),
            _cache,
            new BsftLocalizationOptions());
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find("t1", english, "greeting").Should().BeNull();

        reader.Add(new Translation("greeting", "en", "Hello", "t1"));
        localizer.Resolve("greeting", "en").Should().Be("Hello");

        var value = lookup.Find("t1", english, "greeting");

        value.Should().Be("Hello");
    }

    [Fact]
    public void Find_HitAlreadyInTheCache_IsCheckedBeforeTheMissCache()
    {
        var reader = new FakeTranslationReader();
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find("t1", english, "greeting").Should().BeNull();
        _cache.Set(new TranslationCacheKey("t1", "en", "greeting"), "Hello", TimeSpan.FromMinutes(1));

        var value = lookup.Find("t1", english, "greeting");

        value.Should().Be("Hello");
        reader.KeyReads.Should().Be(1);
    }

    [Fact]
    public void Find_MissCalledAgainWithinTheLifetime_DoesNotReadTheStore()
    {
        var reader = new FakeTranslationReader();
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find(null, english, "missing");
        _time.Advance(Lifetime - TimeSpan.FromTicks(1));
        var value = lookup.Find(null, english, "missing");

        value.Should().BeNull();
        reader.KeyReads.Should().Be(1);
    }

    [Fact]
    public void Find_MissCalledAfterTheLifetime_ReadsTheStoreAgain()
    {
        var reader = new FakeTranslationReader();
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find(null, english, "missing");
        _time.Advance(Lifetime);
        lookup.Find(null, english, "missing");

        reader.KeyReads.Should().Be(2);
    }

    [Fact]
    public void Find_KeyAddedAfterAMissWithinTheLifetime_StaysUnseenUntilTheLifetimeLapses()
    {
        var reader = new FakeTranslationReader();
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find(null, english, "greeting").Should().BeNull();
        reader.Add(new Translation("greeting", "en", "Hello", null));

        _time.Advance(Lifetime - TimeSpan.FromTicks(1));
        lookup.Find(null, english, "greeting").Should().BeNull();

        _time.Advance(TimeSpan.FromTicks(1));
        lookup.Find(null, english, "greeting").Should().Be("Hello");
    }

    [Fact]
    public void Find_KeyLongerThanMaxKeyLength_ThrowsBeforeAnyCacheOrStoreAccess()
    {
        var reader = new FakeTranslationReader(new Translation(new string('k', 9), "en", "Too long", null));
        var lookup = CreateLookup(reader, out var misses, maxKeyLength: 8);

        var act = () => lookup.Find(null, new CultureInfo("en"), new string('k', 9));

        act.Should().Throw<ArgumentException>().WithParameterName("key");
        reader.KeyReads.Should().Be(0);
        misses.Count.Should().Be(0);
    }

    [Fact]
    public void Find_KeyOfExactlyMaxKeyLength_IsAccepted()
    {
        var key = new string('k', 8);
        var reader = new FakeTranslationReader(new Translation(key, "en", "Found", null));
        var lookup = CreateLookup(reader, out _, maxKeyLength: 8);

        var value = lookup.Find(null, new CultureInfo("en"), key);

        value.Should().Be("Found");
    }

    [Fact]
    public void Find_KeySpelledInAnotherCase_ResolvesLikeTheLocalizerButIsReadAgainAndNotCached()
    {
        var reader = new FakeTranslationReader(new Translation("app.title", "en", "Main title", "t1"));
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find("t1", english, "app.title").Should().Be("Main title");
        var readsBefore = reader.KeyReads;

        lookup.Find("t1", english, "APP.TITLE").Should().Be("Main title");
        lookup.Find("t1", english, "APP.TITLE").Should().Be("Main title");

        reader.KeyReads.Should().Be(readsBefore + 2);
        _cache.TryGetValue(new TranslationCacheKey("t1", "en", "APP.TITLE"), out _).Should().BeFalse();
        _cache.TryGetValue(new TranslationCacheKey("t1", "en", "app.title"), out _).Should().BeTrue();
    }

    [Fact]
    public void Find_UnconfiguredLocaleHit_IsNotCachedAndReadsTheStoreEveryTime()
    {
        var reader = new FakeTranslationReader(new Translation("greeting", "fr", "Bonjour", "t1"));
        var lookup = CreateLookup(reader, out var misses);
        var french = new CultureInfo("fr");

        lookup.Find("t1", french, "greeting").Should().Be("Bonjour");
        lookup.Find("t1", french, "greeting").Should().Be("Bonjour");

        reader.KeyReads.Should().Be(2);
        _cache.TryGetValue(new TranslationCacheKey("t1", "fr", "greeting"), out _).Should().BeFalse();
        misses.Count.Should().Be(0);
    }

    [Fact]
    public void Find_UnconfiguredLocaleMiss_IsNotRememberedAndReadsTheStoreEveryTime()
    {
        var reader = new FakeTranslationReader();
        var lookup = CreateLookup(reader, out var misses);
        var french = new CultureInfo("fr");

        lookup.Find("t1", french, "missing").Should().BeNull();
        lookup.Find("t1", french, "missing").Should().BeNull();

        reader.KeyReads.Should().Be(2);
        misses.Count.Should().Be(0);
    }

    [Fact]
    public void Find_ConfiguredLocaleMiss_IsRememberedAndNotReadAgainWithinTheLifetime()
    {
        var reader = new FakeTranslationReader();
        var lookup = CreateLookup(reader, out var misses);
        var english = new CultureInfo("en");

        lookup.Find("t1", english, "missing").Should().BeNull();
        lookup.Find("t1", english, "missing").Should().BeNull();

        reader.KeyReads.Should().Be(1);
        misses.Count.Should().Be(1);
    }

    [Fact]
    public void Find_ConfiguredLocaleHit_IsCachedAndNotReadAgain()
    {
        var reader = new FakeTranslationReader(new Translation("greeting", "en", "Hello", "t1"));
        var lookup = CreateLookup(reader);
        var english = new CultureInfo("en");

        lookup.Find("t1", english, "greeting").Should().Be("Hello");
        lookup.Find("t1", english, "greeting").Should().Be("Hello");

        reader.KeyReads.Should().Be(1);
    }

    [Fact]
    public void Find_LocaleHeldBySeedOnly_IsTreatedAsConfiguredAndCached()
    {
        var seed = new TranslationSeed();
        seed.Add("sv", "greeting", "Hej");
        var reader = new FakeTranslationReader();
        var lookup = CreateLookup(reader, out var misses, seed: seed);
        var swedish = new CultureInfo("sv");

        lookup.Find(null, swedish, "greeting").Should().Be("Hej");
        lookup.Find(null, swedish, "greeting").Should().Be("Hej");

        reader.KeyReads.Should().Be(1);
        misses.Count.Should().Be(0);
    }

    [Fact]
    public void Find_FallbackHit_IsCachedUnderAConfiguredSpellingOfTheLocale()
    {
        var seed = new TranslationSeed();
        seed.Add("sv", "other", "Annat");
        var reader = new FakeTranslationReader(new Translation("greeting", "en", "Hello", null));
        var lookup = CreateLookup(reader, seed: seed);
        var swedish = new CultureInfo("sv");

        lookup.Find(null, swedish, "greeting").Should().Be("Hello");
        lookup.Find(null, swedish, "greeting").Should().Be("Hello");

        reader.KeyReads.Should().Be(1);
    }

    private static TimeSpan Lifetime => TimeSpan.FromSeconds(30);

    private TranslationLookup CreateLookup(
        ITranslationReader reader,
        string defaultLocale = "en",
        int maxKeyLength = 256,
        TranslationSeed? seed = null)
        => CreateLookup(reader, out _, defaultLocale, maxKeyLength, seed);

    private TranslationLookup CreateLookup(
        ITranslationReader reader,
        out TranslationMissCache misses,
        string defaultLocale = "en",
        int maxKeyLength = 256,
        TranslationSeed? seed = null)
    {
        var options = new BsftLocalizationOptions
        {
            DefaultLocale = defaultLocale,
            FallbackLocale = "en",
            MissCacheLifetime = Lifetime,
            MaxKeyLength = maxKeyLength
        };
        var chain = new TranslationChain(
            TranslationReaderFactory.ScopeFactoryFor(reader),
            new HttpContextAccessor(),
            seed ?? new TranslationSeed(),
            _cache,
            options);
        misses = new TranslationMissCache(Options.Create(options), _time);

        return new TranslationLookup(chain, misses, Options.Create(options));
    }
}
