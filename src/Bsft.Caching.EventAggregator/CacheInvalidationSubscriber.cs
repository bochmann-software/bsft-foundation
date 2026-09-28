using Microsoft.Extensions.Hosting;
using IEventAggregator = Bsft.EventAggregator.IEventAggregator;
using Bsft.Caching.Invalidation;

namespace Bsft.Caching.EventAggregator;

/// <summary>
/// Bridges the in-process event aggregator to the cache invalidator: while the host runs, every
/// published <see cref="CacheKeyInvalidatedEvent"/> and <see cref="CacheTagInvalidatedEvent"/>
/// evicts the corresponding entries. This lets a command handler invalidate cached query results
/// by publishing an event instead of depending on the caching implementation.
/// </summary>
/// <remarks>
/// The event aggregator is in-process, so an event evicts entries in the publishing instance only.
/// Fleet-wide invalidation of a shared distributed cache needs an out-of-process transport that
/// republishes the event on every instance.
/// </remarks>
public sealed class CacheInvalidationSubscriber : IHostedService
{
    private readonly IEventAggregator _eventAggregator;
    private readonly ICacheInvalidator _invalidator;
    private readonly Func<CacheKeyInvalidatedEvent, Task> _keyHandler;
    private readonly Func<CacheTagInvalidatedEvent, Task> _tagHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheInvalidationSubscriber"/> class.
    /// </summary>
    /// <param name="eventAggregator">The event aggregator to subscribe to.</param>
    /// <param name="invalidator">The invalidator performing the eviction.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public CacheInvalidationSubscriber(
        IEventAggregator eventAggregator,
        ICacheInvalidator invalidator)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));

        // The delegate instances are kept so StopAsync can unsubscribe exactly what StartAsync
        // subscribed; the aggregator removes handlers by reference equality.
        _keyHandler = OnKeyInvalidatedAsync;
        _tagHandler = OnTagInvalidatedAsync;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _eventAggregator.Subscribe(_keyHandler);
        _eventAggregator.Subscribe(_tagHandler);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _eventAggregator.Unsubscribe(_keyHandler);
        _eventAggregator.Unsubscribe(_tagHandler);

        return Task.CompletedTask;
    }

    // The event aggregator rejects a null event before reaching a handler, so these do not guard.
    private Task OnKeyInvalidatedAsync(CacheKeyInvalidatedEvent invalidation) =>
        _invalidator.InvalidateKeyAsync(invalidation.Key);

    private Task OnTagInvalidatedAsync(CacheTagInvalidatedEvent invalidation) =>
        _invalidator.InvalidateTagAsync(invalidation.Tag);
}
