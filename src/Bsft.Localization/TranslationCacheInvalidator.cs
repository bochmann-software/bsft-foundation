using Bsft.EventAggregator;
using Microsoft.Extensions.Caching.Memory;

namespace Bsft.Localization;

/// <summary>
/// Subscribes to <see cref="TranslationsUpdatedEvent"/> and clears the translation memory cache
/// so that subsequent lookups fetch fresh data from the database.
/// </summary>
public sealed class TranslationCacheInvalidator : IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IMemoryCache _cache;
    private readonly Func<TranslationsUpdatedEvent, Task> _handler;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationCacheInvalidator"/> class.
    /// </summary>
    /// <param name="eventAggregator">The event aggregator to subscribe to.</param>
    /// <param name="cache">The memory cache to clear when translations are updated.</param>
    public TranslationCacheInvalidator(IEventAggregator eventAggregator, IMemoryCache cache)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        _handler = OnTranslationsUpdatedAsync;
        _eventAggregator.Subscribe(_handler);
    }

    private Task OnTranslationsUpdatedAsync(TranslationsUpdatedEvent _)
    {
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _eventAggregator.Unsubscribe(_handler);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
