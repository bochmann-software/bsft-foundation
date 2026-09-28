using Bsft.EventAggregator;
using Bsft.EventAggregator.Saga;
using Bsft.EventAggregator.Saga.Timeouts;
using Bsft.Testing.Time;

namespace Bsft.Testing.Sagas;

/// <summary>
/// A running saga engine a test drives by hand: feed it events, move its clock, and read the state
/// and the pending timers back.
/// </summary>
/// <remarks>
/// The background dispatcher that fires due timeouts in production is never started here. Time only
/// moves when the test moves it, and timers only fire when the test drains them, so a saga that
/// waits three days in production is asserted in microseconds and never depends on a poll interval.
/// </remarks>
public interface ISagaTestHarness : IDisposable
{
    /// <summary>Gets the container the engine runs in, for resolving a collaborator or a double.</summary>
    IServiceProvider Services { get; }

    /// <summary>Gets the aggregator the saga subscribes to.</summary>
    IEventAggregator Events { get; }

    /// <summary>Gets the store holding the saga instances.</summary>
    ISagaStore Store { get; }

    /// <summary>Gets the store holding the scheduled timeouts.</summary>
    ISagaTimeoutStore Timeouts { get; }

    /// <summary>Gets the clock the engine reads, so a test can also set an absolute instant.</summary>
    ITestClock Clock { get; }

    /// <summary>Publishes an event into the engine, which drives the matching saga step.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when every subscriber has run.</returns>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the saga type name the engine stores instances of the given flow under.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <typeparam name="TStartEvent">The event the flow starts with.</typeparam>
    /// <returns>The saga type name.</returns>
    string GetSagaType<TState, TStartEvent>()
        where TState : new();

    /// <summary>Reads back a saga instance, or <c>null</c> when it never started or has completed.</summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <typeparam name="TStartEvent">The event the flow starts with.</typeparam>
    /// <param name="correlationKey">The correlation key of the instance.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The instance, or null.</returns>
    Task<SagaInstance<TState>?> FindSagaAsync<TState, TStartEvent>(
        string correlationKey,
        CancellationToken cancellationToken = default)
        where TState : new();

    /// <summary>Lists every timeout that is still waiting to fire, however far in the future.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The pending timeouts.</returns>
    Task<IReadOnlyList<SagaTimeout>> ListPendingTimeoutsAsync(CancellationToken cancellationToken = default);

    /// <summary>Fires every timeout that is already due, without moving the clock.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of timeouts fired or discarded by the catch-up policy.</returns>
    Task<int> DrainTimeoutsAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves the clock forward and fires everything that became due.</summary>
    /// <param name="by">How far to move forward. Must not be negative.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of timeouts fired or discarded by the catch-up policy.</returns>
    Task<int> AdvanceTimeAsync(TimeSpan by, CancellationToken cancellationToken = default);
}
