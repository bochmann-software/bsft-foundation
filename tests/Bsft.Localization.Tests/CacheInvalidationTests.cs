using Bsft.EventAggregator;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Bsft.Localization.Tests;

public class CacheInvalidationTests : IDisposable
{
    private readonly MemoryCache _cache;

    public CacheInvalidationTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    [Fact]
    public async Task TranslationsUpdatedEvent_ClearsCache()
    {
        var eventAggregator = new SimpleEventAggregator();
        using var invalidator = new TranslationCacheInvalidator(eventAggregator, _cache);

        _cache.Set("bsft:i18n:global:en:greeting", "Hello!");
        _cache.TryGetValue("bsft:i18n:global:en:greeting", out _).Should().BeTrue();

        await eventAggregator.PublishAsync(new TranslationsUpdatedEvent());

        _cache.TryGetValue("bsft:i18n:global:en:greeting", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TranslationsUpdatedEvent_MultipleCacheEntries_AllCleared()
    {
        var eventAggregator = new SimpleEventAggregator();
        using var invalidator = new TranslationCacheInvalidator(eventAggregator, _cache);

        _cache.Set("bsft:i18n:global:en:key1", "v1");
        _cache.Set("bsft:i18n:global:de:key1", "v2");
        _cache.Set("bsft:i18n:t1:en:key1", "v3");

        await eventAggregator.PublishAsync(new TranslationsUpdatedEvent());

        _cache.TryGetValue("bsft:i18n:global:en:key1", out _).Should().BeFalse();
        _cache.TryGetValue("bsft:i18n:global:de:key1", out _).Should().BeFalse();
        _cache.TryGetValue("bsft:i18n:t1:en:key1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_Unsubscribes_FromEventAggregator()
    {
        var eventAggregator = new SimpleEventAggregator();
        var invalidator = new TranslationCacheInvalidator(eventAggregator, _cache);

        invalidator.Dispose();

        // After disposal the invalidator must have unsubscribed, so a published update no longer
        // clears the cache entry.
        _cache.Set("bsft:i18n:global:en:test", "value");
        await eventAggregator.PublishAsync(new TranslationsUpdatedEvent());

        _cache.TryGetValue("bsft:i18n:global:en:test", out _).Should()
            .BeTrue("a disposed invalidator must not clear the cache");
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var eventAggregator = new SimpleEventAggregator();
        var invalidator = new TranslationCacheInvalidator(eventAggregator, _cache);

        invalidator.Dispose();

        // Second dispose should be idempotent
        var act = () => invalidator.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullEventAggregator_ThrowsArgumentNullException()
    {
        var act = () => new TranslationCacheInvalidator(null!, _cache);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("eventAggregator");
    }

    [Fact]
    public void Constructor_NullCache_ThrowsArgumentNullException()
    {
        var eventAggregator = new SimpleEventAggregator();

        var act = () => new TranslationCacheInvalidator(eventAggregator, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cache");
    }

    [Fact]
    public void TranslationsUpdatedEvent_LocaleAndTenantId_PropertiesAreAccessible()
    {
        // The event carries the narrowing a publisher may state.
        var evt = new TranslationsUpdatedEvent { Locale = "en", TenantId = "tenant-1" };

        evt.Locale.Should().Be("en");
        evt.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void TranslationsUpdatedEvent_NullProperties_DefaultToNull()
    {
        // Both are optional, and absent means "everything".
        var evt = new TranslationsUpdatedEvent();

        evt.Locale.Should().BeNull();
        evt.TenantId.Should().BeNull();
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
