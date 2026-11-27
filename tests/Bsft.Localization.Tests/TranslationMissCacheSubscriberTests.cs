using System.Globalization;
using Bsft.EventAggregator;
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
/// The miss cache is forgotten when translations are published as updated, so a key that was missed
/// and has since been added is found by the next lookup rather than after the miss lifetime.
/// </summary>
public class TranslationMissCacheSubscriberTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FakeTimeProvider _time = new(Start);

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task TranslationsUpdatedEvent_ClearsEveryRememberedMiss()
    {
        var misses = CreateMisses();
        var eventAggregator = new SimpleEventAggregator();
        using var invalidator = new TranslationMissCacheSubscriber(eventAggregator, misses);
        misses.TryAdd(new TranslationCacheKey(null, "en", "greeting")).Should().BeTrue();
        misses.TryAdd(new TranslationCacheKey("t1", "en", "farewell")).Should().BeTrue();

        await eventAggregator.PublishAsync(new TranslationsUpdatedEvent { Locale = "en", TenantId = "t1" });

        misses.Count.Should().Be(0);
        misses.Contains(new TranslationCacheKey(null, "en", "greeting")).Should().BeFalse();
        misses.Contains(new TranslationCacheKey("t1", "en", "farewell")).Should().BeFalse();
    }

    [Fact]
    public async Task Find_KeyMissedBeforeAnUpdate_IsReadFromTheStoreAgainRightAfterTheEvent()
    {
        var reader = new FakeTranslationReader();
        var eventAggregator = new SimpleEventAggregator();
        var lookup = CreateLookup(reader, out var misses);
        using var invalidator = new TranslationMissCacheSubscriber(eventAggregator, misses);
        var english = new CultureInfo("en");

        lookup.Find(null, english, "greeting").Should().BeNull();
        reader.Add(new Translation("greeting", "en", "Hello", null));

        lookup.Find(null, english, "greeting").Should().BeNull("the miss is still remembered before the event");
        reader.KeyReads.Should().Be(1);

        await eventAggregator.PublishAsync(new TranslationsUpdatedEvent());

        var value = lookup.Find(null, english, "greeting");

        value.Should().Be("Hello");
        reader.KeyReads.Should().Be(2);
    }

    [Fact]
    public async Task Dispose_Unsubscribes_SoAPublishedUpdateNoLongerClearsTheMisses()
    {
        var misses = CreateMisses();
        var eventAggregator = new SimpleEventAggregator();
        var invalidator = new TranslationMissCacheSubscriber(eventAggregator, misses);
        misses.TryAdd(new TranslationCacheKey(null, "en", "greeting"));

        invalidator.Dispose();
        await eventAggregator.PublishAsync(new TranslationsUpdatedEvent());

        misses.Count.Should().Be(1);
    }

    private TranslationMissCache CreateMisses()
        => new(Options.Create(new BsftLocalizationOptions()), _time);

    private TranslationLookup CreateLookup(ITranslationReader reader, out TranslationMissCache misses)
    {
        var options = new BsftLocalizationOptions();
        var chain = new TranslationChain(
            TranslationReaderFactory.ScopeFactoryFor(reader),
            new HttpContextAccessor(),
            new TranslationSeed(),
            _cache,
            options);
        misses = new TranslationMissCache(Options.Create(options), _time);

        return new TranslationLookup(chain, misses, Options.Create(options));
    }
}
