using Bsft.EventAggregator;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Bsft.Localization.Tests;

public class TranslationCacheInvalidatorHostedServiceTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public void Constructor_NullInvalidator_ThrowsArgumentNullException()
    {
        var act = () => new TranslationCacheInvalidatorHostedService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("invalidator");
    }

    [Fact]
    public async Task StartAsync_CompletesSuccessfully()
    {
        var eventAggregator = new SimpleEventAggregator();
        using var invalidator = new TranslationCacheInvalidator(eventAggregator, _cache);
        var service = new TranslationCacheInvalidatorHostedService(invalidator);

        await service.StartAsync(CancellationToken.None);
        // No exception expected
    }

    [Fact]
    public async Task StopAsync_DisposesInvalidator_NoLongerReceivesEvents()
    {
        var eventAggregator = new SimpleEventAggregator();
        var invalidator = new TranslationCacheInvalidator(eventAggregator, _cache);
        var service = new TranslationCacheInvalidatorHostedService(invalidator);

        _cache.Set("test-key", "test-value");
        await service.StopAsync(CancellationToken.None);

        // After stop, publishing should not crash and cache entry remains (invalidator disposed)
        await eventAggregator.PublishAsync(new TranslationsUpdatedEvent());
        _cache.TryGetValue("test-key", out _).Should().BeTrue();
    }

    public void Dispose() => _cache.Dispose();
}
