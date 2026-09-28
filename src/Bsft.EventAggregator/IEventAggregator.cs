namespace Bsft.EventAggregator;

/// <summary>
/// Provides a publish/subscribe mechanism for in-process event communication.
/// </summary>
public interface IEventAggregator
{
    /// <summary>
    /// Subscribes an asynchronous handler for events of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <remarks>
    /// A subscription lasts until it is unsubscribed, and the aggregator holds the handler strongly
    /// for that whole time. Since the shipped aggregator is a singleton, a subscriber that goes out of
    /// use without unsubscribing is kept alive by it and keeps being invoked on every later publish.
    /// Whoever subscribes therefore owns unsubscribing, which requires holding onto the very delegate
    /// that was passed in: a lambda written inline at the subscribe call cannot be removed again.
    /// </remarks>
    /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
    /// <param name="handler">The asynchronous handler to invoke when the event is published.</param>
    void Subscribe<TEvent>(Func<TEvent, Task> handler);

    /// <summary>
    /// Unsubscribes a previously registered asynchronous handler for events of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to unsubscribe from.</typeparam>
    /// <param name="handler">The asynchronous handler to remove.</param>
    void Unsubscribe<TEvent>(Func<TEvent, Task> handler);

    /// <summary>
    /// Publishes an event to all registered handlers of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to publish.</typeparam>
    /// <param name="event">The event instance to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when all handlers have been invoked.</returns>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a synchronous handler for events of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <remarks>
    /// This is a required member. Implementations must record the wrapper produced for the
    /// supplied <paramref name="handler"/> so that <see cref="Unsubscribe{TEvent}(Action{TEvent})"/>
    /// can locate and remove it by reference. A defaulted forwarding implementation cannot honour
    /// that contract because the wrapper it creates is a distinct delegate instance.
    /// </remarks>
    /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
    /// <param name="handler">The synchronous handler to invoke when the event is published.</param>
    void Subscribe<TEvent>(Action<TEvent> handler);

    /// <summary>
    /// Unsubscribes a previously registered synchronous handler for events of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <remarks>
    /// This is a required member. Implementations must remove the wrapper (or wrappers) recorded by
    /// <see cref="Subscribe{TEvent}(Action{TEvent})"/> for the supplied <paramref name="handler"/>.
    /// </remarks>
    /// <typeparam name="TEvent">The type of event to unsubscribe from.</typeparam>
    /// <param name="handler">The synchronous handler to remove.</param>
    void Unsubscribe<TEvent>(Action<TEvent> handler);

    /// <summary>
    /// Publishes an event synchronously to all registered handlers of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <remarks>
    /// A bridge for callers that have no <c>await</c> to offer — a constructor, a property setter, a
    /// framework callback with a <see langword="void"/> signature. It blocks the calling thread until
    /// every handler has finished, so it costs a thread for the whole delivery, and on a caller that
    /// carries a single-threaded synchronization context it deadlocks outright. Anywhere an
    /// <c>await</c> is possible, use <see cref="PublishAsync{TEvent}"/> instead.
    /// </remarks>
    /// <typeparam name="TEvent">The type of event to publish.</typeparam>
    /// <param name="payload">The event instance to publish.</param>
    void Publish<TEvent>(TEvent payload) => PublishAsync(payload).GetAwaiter().GetResult();
}
