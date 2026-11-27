using Bsft.EventAggregator;
using Microsoft.Extensions.Hosting;

namespace Bsft.Localization;

/// <summary>
/// Subscribes to <see cref="TranslationsUpdatedEvent"/> and forgets every remembered miss, so that a
/// key added by the update is found at once rather than after the miss cache's lifetime.
/// </summary>
/// <remarks>
/// A second subscriber beside <see cref="TranslationCacheInvalidator"/> rather than a parameter of it:
/// the miss cache is internal to this package and the invalidator's constructor is public. It is
/// registered as a hosted service, which is what has it constructed (and so subscribed) at start, and
/// unsubscribes on stop.
/// </remarks>
internal sealed class TranslationMissCacheSubscriber : IHostedService, IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly ITranslationMissCache _misses;
    private readonly Func<TranslationsUpdatedEvent, Task> _handler;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationMissCacheSubscriber"/> class.
    /// </summary>
    /// <param name="eventAggregator">The event aggregator to subscribe to.</param>
    /// <param name="misses">The miss cache to clear when translations are updated.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public TranslationMissCacheSubscriber(IEventAggregator eventAggregator, ITranslationMissCache misses)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _misses = misses ?? throw new ArgumentNullException(nameof(misses));

        _handler = OnTranslationsUpdatedAsync;
        _eventAggregator.Subscribe(_handler);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _eventAggregator.Unsubscribe(_handler);
        _disposed = true;
    }

    private Task OnTranslationsUpdatedAsync(TranslationsUpdatedEvent _)
    {
        _misses.Clear();

        return Task.CompletedTask;
    }
}
