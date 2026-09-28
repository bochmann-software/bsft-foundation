namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Applies the timeout operations a saga step recorded, once that step has finished successfully.
/// </summary>
internal interface ISagaTimeoutScheduler
{
    /// <summary>
    /// Writes the pending schedule and cancel operations of a finished step to the timeout store.
    /// </summary>
    /// <param name="sagaType">The saga type.</param>
    /// <param name="correlationKey">The saga correlation key.</param>
    /// <param name="stepIndex">
    /// The logical index the saga waits at once the recording step is done, which is the index that
    /// step left plus one. Tagging a new timer with the waiting index rather than with the recording
    /// one is what lets <see cref="CancelForStepAsync"/> drop it when the saga leaves that wait.
    /// </param>
    /// <param name="operations">The operations recorded by the step.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task FlushAsync(
        string sagaType,
        string correlationKey,
        int stepIndex,
        IReadOnlyList<PendingSagaTimeout> operations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the timeouts a step left behind once the saga has advanced past it. This is what makes
    /// "the business event arrived first" cancel the reminder automatically.
    /// </summary>
    /// <param name="sagaType">The saga type.</param>
    /// <param name="correlationKey">The saga correlation key.</param>
    /// <param name="stepIndex">The logical index of the step being left.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken cancellationToken = default);

    /// <summary>Drops every timeout of a saga instance that has completed.</summary>
    /// <param name="sagaType">The saga type.</param>
    /// <param name="correlationKey">The saga correlation key.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken cancellationToken = default);
}
