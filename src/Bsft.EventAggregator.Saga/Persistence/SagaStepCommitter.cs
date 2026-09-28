using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Timeouts;

namespace Bsft.EventAggregator.Saga.Persistence;

/// <inheritdoc cref="ISagaStepCommitter"/>
internal sealed class SagaStepCommitter : ISagaStepCommitter
{
    private readonly ISagaStore _store;
    private readonly ISagaTelemetry _telemetry;
    private readonly ISagaTimeoutScheduler _timeouts;
    private readonly ISagaPersistenceWindow _persistence;
    private readonly ISagaInstanceRegistry _instances;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaStepCommitter"/> class.
    /// </summary>
    /// <param name="store">Where saga state is written.</param>
    /// <param name="telemetry">The span and counter surface a commit reports on.</param>
    /// <param name="timeouts">The scheduler pending timers are handed to.</param>
    /// <param name="persistence">The window a commit's writes are staged in.</param>
    /// <param name="instances">The registry a completed instance is removed from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public SagaStepCommitter(
        ISagaStore store,
        ISagaTelemetry telemetry,
        ISagaTimeoutScheduler timeouts,
        ISagaPersistenceWindow persistence,
        ISagaInstanceRegistry instances)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _timeouts = timeouts ?? throw new ArgumentNullException(nameof(timeouts));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The window opens only after the step body has returned. The body therefore never runs with a
    /// database connection pinned to it, while a timer write that fails still takes the state advance
    /// down with it instead of leaving a saga ahead of its reminders.
    /// </remarks>
    public async Task CommitAsync<TState>(
        SagaDefinition<TState> definition,
        SagaInstance<TState> instance,
        IReadOnlyList<PendingSagaTimeout> timeouts,
        int? leavingStepIndex,
        CancellationToken cancellationToken = default) where TState : new()
    {
        if (definition is null)
            throw new ArgumentNullException(nameof(definition));
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));
        if (timeouts is null)
            throw new ArgumentNullException(nameof(timeouts));

        var isFinalStep = IsFinalStep(definition, instance);

        // A saga without a correlation key is never written to any store, so a window would only
        // resolve a database context in order to commit an empty change set. The two calls below run
        // either way: one of them rejects a step that scheduled a reminder for a saga that nothing
        // could ever find again.
        await using ISagaPersistenceWindowScope? window = instance.CorrelationKey is null
            ? null
            : await _persistence.OpenAsync(cancellationToken).ConfigureAwait(false);

        await PersistOrCompleteAsync(definition, instance, isFinalStep, cancellationToken).ConfigureAwait(false);
        await ApplyTimeoutsAsync(instance, timeouts, leavingStepIndex, isFinalStep, cancellationToken).ConfigureAwait(false);

        if (window is not null)
            await window.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether the step that just ran finished the saga, either by calling
    /// <c>MarkComplete()</c> or by being the last step of an auto-completing flow.
    /// </summary>
    private static bool IsFinalStep<TState>(SagaDefinition<TState> definition, SagaInstance<TState> instance)
        where TState : new()
    {
        var maxLogicalIndex = definition.Steps[^1].LogicalStepIndex;
        return instance.IsComplete || (definition.AutoComplete && instance.CurrentStepIndex > maxLogicalIndex);
    }

    /// <summary>
    /// Stages the advanced state of a running saga, or removes a saga the step has finished together
    /// with its pending timers.
    /// </summary>
    /// <param name="definition">The saga definition the step belongs to.</param>
    /// <param name="instance">The saga instance the step advanced.</param>
    /// <param name="isFinalStep">Whether this step finished the saga.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    private async Task PersistOrCompleteAsync<TState>(
        SagaDefinition<TState> definition,
        SagaInstance<TState> instance,
        bool isFinalStep,
        CancellationToken cancellationToken) where TState : new()
    {
        var correlationKey = instance.CorrelationKey;

        if (isFinalStep)
        {
            _telemetry.SagaCompleted(definition.SagaType);
            if (correlationKey is not null)
            {
                _instances.Remove(_instances.KeyFor(definition.SagaType, correlationKey));
                await _store.DeleteAsync(definition.SagaType, correlationKey, cancellationToken).ConfigureAwait(false);

                // A completed saga can never react to a reminder again, so its pending timers go too.
                await _timeouts.CancelAllAsync(definition.SagaType, correlationKey, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (correlationKey is not null)
        {
            await _store.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies the timeout operations a step recorded, after its state has been staged.
    /// </summary>
    /// <remarks>
    /// A step body that throws leaves neither state nor timers behind: nothing has been written yet.
    /// </remarks>
    /// <param name="instance">The saga instance.</param>
    /// <param name="timeouts">The timeout operations the step recorded.</param>
    /// <param name="leavingStepIndex">
    /// The waiting index the saga just left, whose pending timers are dropped, or <c>null</c> for a
    /// saga that has just started.
    /// </param>
    /// <param name="isFinalStep">Whether this step finished the saga, so its timers were already dropped.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    private async Task ApplyTimeoutsAsync<TState>(
        SagaInstance<TState> instance,
        IReadOnlyList<PendingSagaTimeout> timeouts,
        int? leavingStepIndex,
        bool isFinalStep,
        CancellationToken cancellationToken)
    {
        var schedules = timeouts.Any(t => t.Kind == PendingSagaTimeoutKind.Schedule);

        // A saga without a correlation key is never persisted, so a timer could never find it again.
        // This is checked before the completion shortcut below: failing loudly beats dropping the
        // reminder silently, and a step that both schedules and completes is still a programming error.
        if (instance.CorrelationKey is not { } correlationKey)
        {
            if (schedules)
                throw new InvalidOperationException("A saga timeout requires a correlation key on the saga instance.");

            return;
        }

        // The step may have finished the saga — either by calling MarkComplete() or by being the last
        // step of an auto-completing flow. The completion path has already dropped every timer of the
        // instance, so writing a new one now would leave a reminder for a saga that no longer exists.
        if (isFinalStep)
            return;

        // The saga has advanced past the step it was waiting at, so the reminders of that step are
        // obsolete — this is what makes "the business event arrived first" cancel them automatically.
        if (leavingStepIndex is { } left)
            await _timeouts.CancelForStepAsync(instance.SagaType, correlationKey, left, cancellationToken).ConfigureAwait(false);

        if (timeouts.Count > 0)
        {
            // The reminder belongs to the index the saga waits at once this step is done, which is the
            // index it just left plus one — a saga that has just started left the trigger step, index
            // zero. Reading it back off the instance instead would be wrong whenever a step body
            // publishes the next step's event synchronously: the in-process aggregator advances that
            // very instance before the flush, so the timer would be stamped too high and the
            // CancelForStepAsync above would never find it again when the saga leaves its own step.
            var waitingStepIndex = (leavingStepIndex ?? 0) + 1;

            await _timeouts
                .FlushAsync(instance.SagaType, correlationKey, waitingStepIndex, timeouts, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
