namespace Bsft.Mediator.EventAggregator;

/// <summary>
/// An <see cref="INotificationForwarder"/> that republishes every mediator notification through the
/// in-process <see cref="Bsft.EventAggregator.IEventAggregator"/>, so aggregator subscribers and the
/// sagas started by them react to a notification published via <see cref="IMediator.PublishAsync"/>.
/// </summary>
/// <remarks>
/// The notification is published under its static type, which is the type the saga flow host
/// subscribes with, so a saga step waiting for that notification type is triggered.
/// </remarks>
public sealed class EventAggregatorNotificationForwarder : INotificationForwarder
{
    private readonly Bsft.EventAggregator.IEventAggregator _eventAggregator;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventAggregatorNotificationForwarder"/> class.
    /// </summary>
    /// <param name="eventAggregator">The event aggregator to republish notifications through.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="eventAggregator"/> is null.</exception>
    public EventAggregatorNotificationForwarder(Bsft.EventAggregator.IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    }

    /// <inheritdoc />
    public Task ForwardAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification is null)
            throw new ArgumentNullException(nameof(notification));

        return _eventAggregator.PublishAsync(notification, cancellationToken);
    }
}
