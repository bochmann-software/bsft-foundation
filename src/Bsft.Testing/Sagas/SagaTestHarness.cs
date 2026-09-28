using Bsft.EventAggregator;
using Bsft.EventAggregator.Saga;
using Bsft.EventAggregator.Saga.Timeouts;
using Bsft.Testing.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Testing.Sagas;

/// <summary>
/// The default <see cref="ISagaTestHarness"/>. It owns the container the engine runs in and pumps
/// the timeout dispatcher by hand instead of letting it poll.
/// </summary>
public sealed class SagaTestHarness : ISagaTestHarness
{
    private readonly IServiceProvider _services;
    private readonly ISagaBuilderFactory _definitions;
    private readonly ISagaTimeoutDrainer _drainer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaTestHarness"/> class, taking ownership of the
    /// container: disposing the harness disposes it.
    /// </summary>
    /// <param name="services">
    /// A container with the saga engine registered and its flows already started.
    /// </param>
    /// <param name="clock">The clock the engine was registered with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public SagaTestHarness(IServiceProvider services, ITestClock clock)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));

        _definitions = services.GetRequiredService<ISagaBuilderFactory>();
        Events = services.GetRequiredService<IEventAggregator>();
        Store = services.GetRequiredService<ISagaStore>();
        Timeouts = services.GetRequiredService<ISagaTimeoutStore>();

        // The dispatcher is registered as a hosted service, and a hosted service polls. The drain
        // seam is the same instance behind a second role, so pumping it by hand runs the very
        // dispatch logic the application would — catch-up policy, correlation scope, telemetry.
        _drainer = services.GetRequiredService<ISagaTimeoutDrainer>();
    }

    /// <inheritdoc />
    public IServiceProvider Services => _services;

    /// <inheritdoc />
    public IEventAggregator Events { get; }

    /// <inheritdoc />
    public ISagaStore Store { get; }

    /// <inheritdoc />
    public ISagaTimeoutStore Timeouts { get; }

    /// <inheritdoc />
    public ITestClock Clock { get; }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) =>
        Events.PublishAsync(@event, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when no registered flow starts with that event and keeps that state.
    /// </exception>
    public string GetSagaType<TState, TStartEvent>()
        where TState : new()
    {
        foreach (var definition in _definitions.Definitions)
        {
            if (definition is SagaDefinition<TState> typed && typed.InitialEventType == typeof(TStartEvent))
                return typed.SagaType;
        }

        throw new InvalidOperationException(
            $"No saga flow with state '{typeof(TState).Name}' starting with '{typeof(TStartEvent).Name}' is registered.");
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="correlationKey"/> is null, empty, or white space.
    /// </exception>
    public Task<SagaInstance<TState>?> FindSagaAsync<TState, TStartEvent>(
        string correlationKey,
        CancellationToken cancellationToken = default)
        where TState : new()
    {
        if (string.IsNullOrWhiteSpace(correlationKey))
            throw new ArgumentException("The correlation key must not be empty.", nameof(correlationKey));

        return Store.FindAsync<TState>(GetSagaType<TState, TStartEvent>(), correlationKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SagaTimeout>> ListPendingTimeoutsAsync(CancellationToken cancellationToken = default) =>
        Timeouts.ListDueAsync(DateTimeOffset.MaxValue, int.MaxValue, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Draining repeats until nothing is due any more, so a batch limit cannot leave timers behind,
    /// and it counts the timeouts that stopped being due rather than what the dispatcher managed to
    /// claim. The two differ for the ordinary case: firing a timeout advances the saga past the step
    /// that scheduled it, and advancing cancels that step's timers, so the dispatcher loses the race
    /// against itself and reports a delivered timeout as unclaimed.
    /// </para>
    /// <para>
    /// A timeout whose handler throws stays pending — the dispatcher logs it and counts a failed
    /// attempt — so the pass makes no progress and draining stops instead of spinning. It then shows
    /// up in <see cref="ListPendingTimeoutsAsync"/>, where a test can see it.
    /// </para>
    /// </remarks>
    public async Task<int> DrainTimeoutsAsync(CancellationToken cancellationToken = default)
    {
        var fired = 0;

        while (true)
        {
            var before = await GetDueIdsAsync(cancellationToken).ConfigureAwait(false);
            if (before.Count == 0)
                return fired;

            await _drainer.DrainDueAsync(cancellationToken).ConfigureAwait(false);

            var after = await GetDueIdsAsync(cancellationToken).ConfigureAwait(false);
            before.ExceptWith(after);

            if (before.Count == 0)
                return fired;

            fired += before.Count;
        }
    }

    private async Task<HashSet<long>> GetDueIdsAsync(CancellationToken cancellationToken)
    {
        var due = await Timeouts.ListDueAsync(Clock.UtcNow, int.MaxValue, cancellationToken).ConfigureAwait(false);

        return [.. due.Select(timeout => timeout.Id)];
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="by"/> is negative.</exception>
    public Task<int> AdvanceTimeAsync(TimeSpan by, CancellationToken cancellationToken = default)
    {
        Clock.Advance(by);
        return DrainTimeoutsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The dispatcher is a container singleton now, so disposing the provider disposes it too.
        (_services as IDisposable)?.Dispose();
    }
}
