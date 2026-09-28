using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.EventAggregator.Saga.Execution;

/// <summary>
/// Default implementation of <see cref="ISagaEngine"/> that creates saga instances,
/// executes their first step, and persists them to the saga store.
/// </summary>
internal sealed class SagaEngine : ISagaEngine
{
    private readonly ISagaTelemetry _telemetry;
    private readonly ISagaStepCommitter _committer;
    private readonly ISagaStepScopeFactory _stepScopes;

    private enum SagaStepOutcome
    {
        Executed,
        Skipped,
    }

    /// <inheritdoc />
    public IEventAggregator EventAggregator { get; }

    /// <inheritdoc />
    public IServiceProvider Services { get; }

    /// <inheritdoc />
    public ISagaStore Store { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaEngine"/> class.
    /// </summary>
    /// <param name="eventAggregator">The event aggregator for event communication.</param>
    /// <param name="services">The root service provider.</param>
    /// <param name="store">The saga store for persistence.</param>
    /// <param name="options">The engine configuration options. Held by the container rather than read here; the parameter keeps the engine failing fast when the options are not registered at all.</param>
    public SagaEngine(
        IEventAggregator eventAggregator,
        IServiceProvider services,
        ISagaStore store,
        IOptions<BsftSagaOptions> options)
    {
        EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Store = store ?? throw new ArgumentNullException(nameof(store));

        if (options is null)
            throw new ArgumentNullException(nameof(options));

        // Telemetry, the step committer and the step scope factory are resolved from the injected
        // provider (all singletons) rather than taken as constructor parameters, keeping the
        // constructor focused on the core lifecycle dependencies.
        _telemetry = Services.GetRequiredService<ISagaTelemetry>();
        _committer = Services.GetRequiredService<ISagaStepCommitter>();
        _stepScopes = Services.GetRequiredService<ISagaStepScopeFactory>();
    }

    /// <inheritdoc />
    public async Task StartSagaAsync<TState>(SagaDefinition<TState> definition, string? correlationKey, object triggerEvent, CancellationToken cancellationToken = default) where TState : new()
    {
        if (definition is null)
            throw new ArgumentNullException(nameof(definition));
        if (triggerEvent is null)
            throw new ArgumentNullException(nameof(triggerEvent));

        var instance = new SagaInstance<TState>(definition.SagaType, correlationKey, new TState());

        // The trigger step always runs in its own scope, through the same factory the flow host uses.
        // Handing it the root provider instead — which is what this engine holds, being a singleton —
        // made the step resolve scoped services from the root: capturing the correlation id for a
        // timeout throws outright under the scope validation an ASP.NET Core Development host turns
        // on, and captures a single process-lifetime instance when it is off. Going through the
        // factory also restores the caller's ambient correlation id into the fresh scope, so a
        // timeout scheduled here carries the same id it would on the event-driven path.
        //
        // The scope is owned here and disposed only after the commit, so the step's scoped services
        // are still alive while its state advance and its timers are written. Awaiting its disposal
        // tears down async-disposable scoped services (a DbContext resolved in the step among them)
        // instead of leaving them for finalization, which would exhaust the connection pool under
        // sustained saga traffic. The block form of await using is used so ConfigureAwait(false)
        // applies to the scope's asynchronous disposal as well as to its creation.
        var scope = await _stepScopes.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var context = new SagaContext<TState>(instance, EventAggregator, scope.ServiceProvider);
            var step = definition.Steps[0];

            _telemetry.SagaStarted(definition.SagaType);

            using (var stepScope = _telemetry.BeginStep(definition.SagaType, correlationKey, instance.CurrentStepIndex))
            {
                var outcome = await ExecuteStepAsync(context, step, triggerEvent, instance, cancellationToken).ConfigureAwait(false);
                if (outcome == SagaStepOutcome.Skipped)
                    stepScope.Skipped();
            }

            instance.CurrentStepIndex = 1;

            // The same commit the flow host performs, rather than a private copy of it: the trigger
            // step may have scheduled timeouts through ISagaContext, and those have to reach the
            // store together with the state advance instead of being dropped with the context.
            await _committer
                .CommitAsync(definition, instance, context.PendingTimeouts, null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<SagaStepOutcome> ExecuteStepAsync<TState>(SagaContext<TState> context, SagaStep step, object triggerEvent, SagaInstance<TState> instance, CancellationToken cancellationToken = default)
    {
        if (step.Condition is not null)
        {
            var conditionResult = step.EvaluateCondition!(triggerEvent, instance.State);
            if (step.Condition.IsNegated)
                conditionResult = !conditionResult;

            if (!conditionResult)
                return SagaStepOutcome.Skipped;
        }

        await step.Invoke(context, triggerEvent).ConfigureAwait(false);
        return SagaStepOutcome.Executed;
    }
}
