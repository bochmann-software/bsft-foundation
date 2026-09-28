namespace Bsft.Messaging;

/// <summary>
/// Runs the <see cref="IIntegrationEventHandler{TEvent}"/> instances registered for a decoded event.
/// It is the step both consuming paths share: the in-process bus performs it inside a publish, and the
/// consumer host performs it for a message that arrived over a transport.
/// </summary>
/// <remarks>
/// It exists as a seam of its own because the rules it enforces are subtle and must not be allowed to
/// drift apart between the two callers: the handlers run one at a time in registration order, every
/// handler runs even when an earlier one fails, the failures are reported together, and a handler
/// cancelled by a token other than the caller's counts as a failure rather than as the caller having
/// withdrawn.
/// </remarks>
public interface IIntegrationEventHandlerInvoker
{
    /// <summary>
    /// Invokes every handler registered for the event's runtime type, one after another in registration
    /// order, and reports how many there were.
    /// </summary>
    /// <param name="handlerServices">
    /// The service provider the handlers are resolved from. It is the caller's per-delivery scope, so
    /// a handler may take scoped dependencies.
    /// </param>
    /// <param name="integrationEvent">The decoded event to hand to the handlers.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// The number of handlers that were invoked. Zero is not an error: an event may exist for consumers
    /// in other services only.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="Publishing.MessagePublishException">Thrown when at least one handler failed.</exception>
    Task<int> InvokeAllAsync(
        IServiceProvider handlerServices, IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
