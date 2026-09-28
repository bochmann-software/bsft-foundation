namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Manages the lifecycle of saga instances, providing access to the event aggregator, service provider, and saga store.
/// </summary>
public interface ISagaEngine
{
    /// <summary>
    /// Gets the event aggregator used for publishing and subscribing to events.
    /// </summary>
    IEventAggregator EventAggregator { get; }

    /// <summary>
    /// Gets the root service provider for resolving dependencies.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Gets the saga store used for persisting saga instances.
    /// </summary>
    ISagaStore Store { get; }

    /// <summary>
    /// Starts a new saga instance from the given definition and trigger event.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state.</typeparam>
    /// <param name="definition">The saga definition describing the steps to execute.</param>
    /// <param name="correlationKey">The correlation key for this instance, or <c>null</c> if not correlated.</param>
    /// <param name="triggerEvent">The event that triggered this saga.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task StartSagaAsync<TState>(SagaDefinition<TState> definition, string? correlationKey, object triggerEvent, CancellationToken cancellationToken = default) where TState : new();
}
