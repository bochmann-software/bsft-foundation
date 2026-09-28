namespace Bsft.EventAggregator.Saga.Diagnostics;

/// <summary>
/// Emits traces and metrics for saga execution: start and completion counters and a per-step span
/// with duration.
/// </summary>
public interface ISagaTelemetry
{
    /// <summary>Records that a saga instance has started.</summary>
    /// <param name="sagaType">The saga type identifier.</param>
    void SagaStarted(string sagaType);

    /// <summary>Records that a saga instance has completed and is being removed.</summary>
    /// <param name="sagaType">The saga type identifier.</param>
    void SagaCompleted(string sagaType);

    /// <summary>
    /// Begins a telemetry scope for a single saga step. Dispose the scope when the step finishes to
    /// record its duration.
    /// </summary>
    /// <param name="sagaType">The saga type identifier.</param>
    /// <param name="correlationKey">The saga correlation key, recorded as a span tag when present.</param>
    /// <param name="stepIndex">The zero-based index of the step.</param>
    /// <returns>A scope that must be disposed when the step finishes.</returns>
    ISagaStepTelemetryScope BeginStep(string sagaType, string? correlationKey, int stepIndex);

    /// <summary>Records that a durable timeout was scheduled.</summary>
    /// <param name="sagaType">The saga type identifier.</param>
    void TimeoutScheduled(string sagaType);

    /// <summary>Records that a due timeout published its event.</summary>
    /// <param name="sagaType">The saga type identifier.</param>
    void TimeoutFired(string sagaType);

    /// <summary>
    /// Begins a trace span covering the dispatch of one due timeout. Scheduling and cancelling a
    /// timer happen inside a saga step and are already covered by that step's span; firing one runs
    /// on the dispatcher's polling loop, so without this span a trace shows the request that
    /// scheduled the reminder and the step it later wakes with a gap in between.
    /// </summary>
    /// <param name="sagaType">The saga type identifier.</param>
    /// <param name="timeoutName">The name of the timeout being dispatched.</param>
    /// <returns>
    /// A scope to dispose when the dispatch finishes, or <see langword="null"/> when no listener is
    /// sampling the source.
    /// </returns>
    IDisposable? BeginTimeoutDispatch(string sagaType, string timeoutName);

    /// <summary>Records that a pending timeout was cancelled before it fired.</summary>
    /// <param name="sagaType">The saga type identifier.</param>
    void TimeoutCanceled(string sagaType);

    /// <summary>Records that an overdue timeout was discarded by the catch-up policy.</summary>
    /// <param name="sagaType">The saga type identifier.</param>
    void TimeoutMissed(string sagaType);

    /// <summary>
    /// Records that a timeout was retired to the dead-letter state after failing too often. Separate
    /// from <see cref="TimeoutMissed"/> because the two need different responses: a missed timer was
    /// discarded on purpose by the catch-up policy, a retired one is a timer that broke and now needs
    /// somebody to look at it.
    /// </summary>
    /// <param name="sagaType">The saga type identifier.</param>
    void TimeoutDeadLettered(string sagaType);
}
