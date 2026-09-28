using System.Reflection;
using Bsft.Core;
using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Persistence;

namespace Bsft.EventAggregator.Saga.Execution;

/// <summary>
/// Resolves all <see cref="ISagaFlow"/> instances from DI, calls their <see cref="ISagaFlow.Configure"/> method,
/// and subscribes to the event aggregator to start new sagas and advance existing ones. Live
/// instances and the claiming of a waiting step belong to <see cref="SagaInstanceRegistry"/>.
/// </summary>
/// <remarks>
/// A waiting step is claimed through <see cref="SagaInstanceRegistry.ClaimStep"/>, so two publishers
/// reaching it — a request thread with a business event and the timeout dispatcher with a due timer —
/// cannot both advance it. The store read, the condition predicate and the step body all run outside
/// that claim, because its gate is non-reentrant and a step that synchronously publishes the next
/// step's event would otherwise deadlock against it.
/// <para>
/// The trigger step is not gated: it first confirms against the store that no saga exists for the
/// key — the cache is per process and would let a duplicate trigger restart a persisted saga after a
/// restart — and only then publishes the new instance with an atomic <c>TryAdd</c>. That order
/// matters: the instance carries the following step's index from the moment it is published, so
/// anything awaited between the publication and the trigger body would expose a claimable saga whose
/// trigger step has not run. Its body then runs concurrently with an event for the following step by
/// design — that is what makes a step able to publish the next step's event synchronously. A trigger
/// step and a step-1 event for the same key therefore still touch the same state object concurrently
/// once the body has begun.
/// </para>
/// <para>
/// Two application instances triggering the same key at the same moment can both pass that store
/// check. The unique index over saga type and correlation key catches them while both writes are
/// still inserts; the store's optimistic concurrency token does not, because a save re-reads the row
/// it is about to write. Cross-process start deduplication is not solved here.
/// </para>
/// </remarks>
public sealed class SagaFlowHost
{
    private readonly IReadOnlyList<ISagaFlow> _flows;
    private readonly ISagaEngine _engine;
    private readonly ISagaBuilderFactory _factory;
    private readonly ISagaTelemetry _telemetry;
    private readonly ISagaStepCommitter _committer;
    private readonly ISagaStepScopeFactory _stepScopes;

    // Key: "SagaType|CorrelationKey" -> live saga instance, plus the gate that claims a step.
    private readonly ISagaInstanceRegistry _instances;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaFlowHost"/> class.
    /// </summary>
    /// <remarks>
    /// Internal because three of its collaborators are, which is also why
    /// <c>AddBsftSaga</c> registers this type through an explicit factory rather than by
    /// type: the composition root is the one place that may name every part.
    /// </remarks>
    /// <param name="flows">The saga flows the host registered, configured once at start.</param>
    /// <param name="engine">The saga engine for starting and managing saga instances.</param>
    /// <param name="factory">The builder factory that collects saga definitions.</param>
    /// <param name="telemetry">The span and counter surface a step reports on.</param>
    /// <param name="committer">Writes a step's state advance and its pending timers together.</param>
    /// <param name="instances">The registry live saga instances are kept in.</param>
    /// <param name="stepScopes">Opens the scope a step body runs in.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    internal SagaFlowHost(
        IEnumerable<ISagaFlow> flows,
        ISagaEngine engine,
        ISagaBuilderFactory factory,
        ISagaTelemetry telemetry,
        ISagaStepCommitter committer,
        ISagaInstanceRegistry instances,
        ISagaStepScopeFactory stepScopes)
    {
        ArgumentNullException.ThrowIfNull(flows);

        _flows = flows.ToArray();
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _stepScopes = stepScopes ?? throw new ArgumentNullException(nameof(stepScopes));
    }

    /// <summary>
    /// Configures all registered saga flows and subscribes to their trigger events.
    /// Call this method once during application startup.
    /// </summary>
    public void Start()
    {
        foreach (var flow in _flows)
        {
            flow.Configure(_factory);
        }

        foreach (var definitionObj in _factory.Definitions)
        {
            var defType = definitionObj.GetType();
            var stateType = defType.GetGenericArguments()[0];

            var method = typeof(SagaFlowHost)
                .GetMethod(nameof(SubscribeToDefinition), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(stateType);

            method.Invoke(this, [definitionObj]);
        }
    }

    private void SubscribeToDefinition<TState>(SagaDefinition<TState> definition) where TState : new()
    {
        var subscribedTypes = new HashSet<Type>();

        for (var i = 0; i < definition.Steps.Count; i++)
        {
            var step = definition.Steps[i];

            if (!subscribedTypes.Add(step.EventType))
                continue;

            var subscribeMethod = typeof(SagaFlowHost)
                .GetMethod(nameof(SubscribeToEvent), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(typeof(TState), step.EventType);

            subscribeMethod.Invoke(this, [definition]);
        }
    }

    /// <summary>
    /// The number of times an event is re-handled against freshly reloaded saga state after the store
    /// refused its step with a <see cref="PreconditionFailedException"/>. Bounded so a persistently
    /// contended saga surfaces the conflict to the publisher instead of re-running forever.
    /// </summary>
    private const int MaxConflictAttempts = 3;

    private void SubscribeToEvent<TState, TEvent>(SagaDefinition<TState> definition) where TState : new()
    {
        _engine.EventAggregator.Subscribe<TEvent>(async sagaEvent =>
        {
            for (var i = 0; i < definition.Steps.Count; i++)
            {
                var step = definition.Steps[i];
                if (step.EventType != typeof(TEvent))
                    continue;

                var trigger = i == 0;
                await RunWithConflictRetryAsync(attempt => trigger
                    ? HandleTriggerEventAsync(definition, step, sagaEvent!, attempt)
                    : HandleSubsequentEventAsync(definition, step, sagaEvent!, attempt)).ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// Runs one event-handling attempt, re-running it against freshly reloaded state when the store
    /// refuses the step because another writer advanced the saga in the meantime.
    /// </summary>
    /// <param name="attempt">
    /// Runs one handling attempt, given the 1-based attempt number; it reloads the saga through the
    /// store each time it runs.
    /// </param>
    /// <returns>A task that completes once the event has been handled or the retries are exhausted.</returns>
    /// <remarks>
    /// Saga event delivery through the in-process aggregator is fire-and-forget: a handler that throws
    /// loses the event, nothing redelivers it. An optimistic-concurrency refusal is therefore resolved
    /// here rather than lost. Each attempt reloads the saga — its cache entry is dropped as the refusal
    /// unwinds — and runs the step in a fresh scope, so a step body is invoked at least once under
    /// genuine concurrency; step side effects must tolerate re-execution, exactly as at-least-once
    /// messaging requires. The final attempt does not catch, so a persistently contended saga surfaces
    /// the conflict to the publisher. The attempt number lets the handler emit the per-logical-event
    /// telemetry (saga started, step began) once instead of once per retried attempt.
    /// </remarks>
    private static async Task RunWithConflictRetryAsync(Func<int, Task> attempt)
    {
        for (var i = 1; i <= MaxConflictAttempts; i++)
        {
            try
            {
                await attempt(i).ConfigureAwait(false);
                return;
            }
            catch (PreconditionFailedException) when (i < MaxConflictAttempts)
            {
                // The store refused the step: another writer advanced the saga between this attempt's
                // load and its commit. The cache entry was dropped as the exception unwound, so the
                // next attempt reloads the row as it now stands and re-decides against fresh state.
                continue;
            }
        }
    }

    private async Task HandleTriggerEventAsync<TState, TEvent>(
        SagaDefinition<TState> definition, SagaStep step, TEvent sagaEvent, int attempt, CancellationToken cancellationToken = default) where TState : new()
    {
        var correlationKey = ResolveCorrelationKey<TState, TEvent>(step, sagaEvent);
        var key = correlationKey is not null ? _instances.KeyFor(definition.SagaType, correlationKey) : null;

        var instance = new SagaInstance<TState>(definition.SagaType, correlationKey, new TState());
        instance.CurrentStepIndex = 1;

        // Cache the live instance BEFORE executing the step so that reentrant event handlers find it
        // immediately.
        if (correlationKey is not null
            && !await TryStartAsync(definition, correlationKey, instance, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // Owned here and awaited on disposal so async-disposable scoped services (a DbContext among
        // them) are torn down after the awaited step, not left leaking until finalization. The block
        // form of await using is used so ConfigureAwait(false) applies to the scope's asynchronous
        // disposal as well as to its creation.
        var scope = await _stepScopes.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var context = new SagaContext<TState>(instance, _engine.EventAggregator, scope.ServiceProvider);

            if (!IsConditionMet(step, sagaEvent, instance.State))
            {
                // The start condition gated the trigger out, so the saga never starts: emit nothing.
                if (key is not null)
                    _instances.Remove(key);
                return;
            }

            // Emit the logical saga-started and step-began telemetry on the first attempt only: a
            // conflict retry re-handles the same logical event and must not inflate these counts.
            if (attempt == 1)
                _telemetry.SagaStarted(definition.SagaType);
            using var stepScope = attempt == 1 ? _telemetry.BeginStep(definition.SagaType, correlationKey, 0) : null;

            try
            {
                await step.Invoke(context, sagaEvent).ConfigureAwait(false);
                await _committer.CommitAsync(definition, instance, context.PendingTimeouts, null, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The step failed; drop the orphaned cache entry so the saga is not left half-started.
                if (key is not null)
                    _instances.Remove(key);
                throw;
            }
        }
    }

    /// <summary>
    /// Publishes a newly started instance, refusing the start when a saga already exists for the key.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="definition">The definition the saga is starting from.</param>
    /// <param name="correlationKey">The correlation key resolved from the trigger event.</param>
    /// <param name="instance">The instance the trigger step would run on.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when the trigger step may run, <c>false</c> when the saga is already running.</returns>
    private async Task<bool> TryStartAsync<TState>(
        SagaDefinition<TState> definition,
        string correlationKey,
        SagaInstance<TState> instance,
        CancellationToken cancellationToken) where TState : new()
    {
        // The store is consulted BEFORE the instance is published, never after. The cache is per
        // process and volatile, so on its own it only recognises a duplicate for as long as the
        // instance stays cached: after a restart the store is the only thing that still knows the
        // saga exists, and a repeated trigger would otherwise start over on fresh state and
        // overwrite the running saga's row.
        //
        // Publishing first and awaiting the read afterwards would leave a claimable instance sitting
        // at the following step's index for the whole round trip while its trigger step body has not
        // run: a concurrent event for that step claims it, runs against default state and persists
        // the saga, and the resuming trigger then finds that very row and abandons its own start —
        // leaving an initialised-by-nobody saga one step ahead of itself.
        //
        // The read stays outside the claim gate and outside any persistence window, exactly as the
        // one in the subsequent-step path does, so it changes nothing about how a step is claimed or
        // committed.
        if (await _engine.Store.FindAsync<TState>(definition.SagaType, correlationKey, cancellationToken).ConfigureAwait(false) is not null)
            return false;

        var key = _instances.KeyFor(definition.SagaType, correlationKey);

        // TryAdd, not an overwrite, so a duplicate trigger for an already-active saga does not start
        // a second instance and clobber the first. Nothing awaits between here and the trigger step
        // body, so the instance is claimable only while that body actually runs — which is the
        // documented behaviour that lets a step publish the next step's event synchronously.
        return _instances.TryStart(key, instance);
    }

    private static string? ResolveCorrelationKey<TState, TEvent>(SagaStep step, TEvent sagaEvent) where TState : new()
    {
        if (step.CorrelationKeySelector is Func<TEvent, string> selector)
            return selector(sagaEvent);
        return null;
    }

    private static bool IsConditionMet<TEvent, TState>(SagaStep step, TEvent sagaEvent, TState state)
    {
        if (step.Condition is null)
            return true;

        var result = step.EvaluateCondition!(sagaEvent, state);
        return step.Condition.IsNegated ? !result : result;
    }

    private async Task HandleSubsequentEventAsync<TState, TEvent>(
        SagaDefinition<TState> definition, SagaStep step, TEvent sagaEvent, int attempt, CancellationToken cancellationToken = default) where TState : new()
    {
        var correlationKey = ResolveCorrelationKey<TState, TEvent>(step, sagaEvent);
        if (correlationKey is null)
            return;

        var key = _instances.KeyFor(definition.SagaType, correlationKey);

        // Reading the instance can hit the store, so it stays outside the claim: holding a shared
        // stripe across a database round trip would serialize unrelated sagas behind network I/O.
        var resolved = await ResolveInstanceAsync<TState>(definition.SagaType, correlationKey, cancellationToken).ConfigureAwait(false);
        if (resolved is null || resolved.IsComplete || resolved.CurrentStepIndex != step.LogicalStepIndex)
            return;

        // Emit the step-began telemetry on the first attempt only: a conflict retry re-handles the same
        // logical event and must not inflate the step count.
        using var stepScope = attempt == 1
            ? _telemetry.BeginStep(definition.SagaType, correlationKey, step.LogicalStepIndex)
            : null;

        // The condition is an arbitrary user predicate, so it stays outside the claim too: a
        // non-reentrant gate must never be held while foreign code runs.
        if (!IsConditionMet(step, sagaEvent, resolved.State))
        {
            stepScope?.Skipped();
            return;
        }

        var claim = _instances.ClaimStep(key, resolved, step.LogicalStepIndex, cancellationToken);
        if (claim is null)
        {
            // Another publisher advanced this step between the check above and the claim — a business
            // event and a due timer arriving together. Exactly one of them proceeds.
            stepScope?.Skipped();
            return;
        }

        // The step runs on the instance the claim advanced, which is not always the one resolved
        // above: the registry re-reads the cache behind its gate. Running on the resolved object
        // instead would persist a state still sitting at the waiting index while the cache has moved
        // past it, and nothing afterwards reconciles the two.
        var instance = claim.Instance;
        var previousStepIndex = claim.PreviousStepIndex;

        // Owned here and awaited on disposal so async-disposable scoped services (a DbContext among
        // them) are torn down after the awaited step, not left leaking until finalization. The block
        // form of await using is used so ConfigureAwait(false) applies to the scope's asynchronous
        // disposal as well as to its creation.
        var scope = await _stepScopes.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var context = new SagaContext<TState>(instance, _engine.EventAggregator, scope.ServiceProvider);

            try
            {
                await step.Invoke(context, sagaEvent).ConfigureAwait(false);
                await _committer
                    .CommitAsync(definition, instance, context.PendingTimeouts, previousStepIndex, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Give the claim back and drop the cached instance so the next event re-reads the store
                // rather than trusting a half-applied cache entry. A failure inside the persist window
                // leaves nothing committed, so memory and store agree again afterwards.
                _instances.ReleaseClaim(key, instance, previousStepIndex);
                throw;
            }
        }
    }

    private async Task<SagaInstance<TState>?> ResolveInstanceAsync<TState>(
        string sagaType, string correlationKey, CancellationToken cancellationToken = default) where TState : new()
    {
        var key = _instances.KeyFor(sagaType, correlationKey);

        // Try the in-memory cache first (handles reentrant publishes),
        // then fall back to the persistent store.
        if (_instances.FindCached<TState>(key) is { } hit)
            return hit;

        return await _engine.Store.FindAsync<TState>(sagaType, correlationKey, cancellationToken).ConfigureAwait(false);
    }
}
