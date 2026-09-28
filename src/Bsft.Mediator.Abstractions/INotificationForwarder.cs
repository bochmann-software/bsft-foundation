namespace Bsft.Mediator;

/// <summary>
/// Receives every notification the mediator publishes once notification forwarding is enabled,
/// so a notification can additionally reach a bus outside the mediator (for example the in-process
/// event aggregator and the sagas subscribed to it).
/// </summary>
/// <remarks>
/// The seam keeps the mediator package free of any dependency on a concrete bus: the mediator only
/// knows this contract, and a bridge package supplies the implementation.
/// </remarks>
public interface INotificationForwarder
{
    /// <summary>
    /// Forwards a published notification to the target bus.
    /// </summary>
    /// <typeparam name="TNotification">The static notification type. It is deliberately the type the
    /// caller published under — the same type the mediator resolves local
    /// <see cref="INotificationHandler{TNotification}"/> instances for — so a subscriber that is
    /// registered for the concrete notification type receives the notification.</typeparam>
    /// <param name="notification">The notification to forward.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the target bus has delivered the notification.</returns>
    Task ForwardAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
