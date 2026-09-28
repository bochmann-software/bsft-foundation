using Bsft.EventAggregator.Saga.Timeouts;

namespace Bsft.EventAggregator.Saga.Persistence;

/// <summary>
/// Commits everything a finished saga step produced — the advanced state and the timeout operations
/// the step recorded — so that the two become durable together or not at all.
/// </summary>
/// <remarks>
/// Both entry points into a saga step share this: the event-driven <see cref="Bsft.EventAggregator.Saga.Execution.SagaFlowHost"/> and the
/// direct <see cref="ISagaEngine.StartSagaAsync{TState}"/> API. Keeping the policy in one place is
/// what stops the two from drifting apart — a saga started directly used to advance its state while
/// silently dropping every timeout its trigger step scheduled.
/// </remarks>
internal interface ISagaStepCommitter
{
    /// <summary>
    /// Persists the advanced state of a step and applies the timeout operations it recorded, inside
    /// one persistence window.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="definition">The saga definition the step belongs to.</param>
    /// <param name="instance">The saga instance the step advanced.</param>
    /// <param name="timeouts">The timeout operations the step recorded.</param>
    /// <param name="leavingStepIndex">
    /// The waiting index the saga just left, whose pending timers are dropped, or <c>null</c> for a
    /// saga that has just started.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task CommitAsync<TState>(
        SagaDefinition<TState> definition,
        SagaInstance<TState> instance,
        IReadOnlyList<PendingSagaTimeout> timeouts,
        int? leavingStepIndex,
        CancellationToken cancellationToken = default) where TState : new();
}
