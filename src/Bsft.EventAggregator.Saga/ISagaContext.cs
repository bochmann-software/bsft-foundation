namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Provides contextual information and services to a saga step handler during execution.
/// </summary>
/// <typeparam name="TState">The type of the saga state.</typeparam>
public interface ISagaContext<TState>
{
    /// <summary>
    /// Gets the current state of the saga.
    /// </summary>
    TState State { get; }

    /// <summary>
    /// Gets the correlation key that identifies this saga instance.
    /// </summary>
    string? CorrelationKey { get; }

    /// <summary>
    /// Gets the service provider for resolving dependencies.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Gets a required service of type <typeparamref name="T"/> from the service provider.
    /// </summary>
    /// <typeparam name="T">The type of service to retrieve.</typeparam>
    /// <returns>The service instance.</returns>
    T GetService<T>() where T : notnull;

    /// <summary>
    /// Publishes an event through the event aggregator.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to publish.</typeparam>
    /// <param name="event">The event instance to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the saga instance as complete, causing it to be removed from the store after the current step finishes.
    /// </summary>
    void MarkComplete();

    /// <summary>
    /// Schedules a durable timeout that publishes <paramref name="timeoutEvent"/> after the delay has
    /// elapsed. Several timeouts can be pending per step; they are addressed individually by name.
    /// </summary>
    /// <typeparam name="TEvent">The type of event the timeout publishes.</typeparam>
    /// <param name="name">The name of the timeout, unique within this saga instance.</param>
    /// <param name="delay">The business-time delay after which the timeout fires.</param>
    /// <param name="timeoutEvent">The event to publish when the timeout fires.</param>
    /// <param name="calendarKey">
    /// The key of the business calendar that interprets the delay, or <c>null</c> for the default
    /// calendar on which every day is a working day.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeoutEvent"/> is null.</exception>
    void ScheduleTimeout<TEvent>(string name, TimeSpan delay, TEvent timeoutEvent, string? calendarKey = null);

    /// <summary>
    /// Schedules a durable timeout that fires at an absolute instant. The business calendar is not
    /// consulted: the caller already decided when the timeout is due.
    /// </summary>
    /// <typeparam name="TEvent">The type of event the timeout publishes.</typeparam>
    /// <param name="name">The name of the timeout, unique within this saga instance.</param>
    /// <param name="dueAt">The instant at which the timeout fires.</param>
    /// <param name="timeoutEvent">The event to publish when the timeout fires.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeoutEvent"/> is null.</exception>
    void ScheduleTimeout<TEvent>(string name, DateTimeOffset dueAt, TEvent timeoutEvent);

    /// <summary>
    /// Cancels a pending timeout by name. Cancelling a timeout that has already fired is a no-op —
    /// the published event is dropped by the step guard because the saga has moved on.
    /// </summary>
    /// <param name="name">The name of the timeout to cancel.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    void CancelTimeout(string name);

    /// <summary>Cancels every pending timeout of this saga instance.</summary>
    void CancelAllTimeouts();
}
