namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Runs a single dispatch pass over the due saga timeouts on demand, instead of waiting for the
/// dispatcher's polling loop to come round.
/// </summary>
/// <remarks>
/// <para>
/// This is the supported seam for a test that models a process spanning days or weeks: advance the
/// <see cref="TimeProvider"/> the engine was registered with, then drain, and the timers fire
/// without anyone waiting on the wall clock. It is registered on the same instance the host runs as
/// a background service, so draining exercises the real catch-up policy, correlation scope and
/// telemetry rather than a reimplementation that can drift.
/// </para>
/// <para>
/// An operator can use it for the same reason a test can: it is a legitimate "flush the due timers
/// now" operation.
/// </para>
/// </remarks>
public interface ISagaTimeoutDrainer
{
    /// <summary>Runs one dispatch pass over the timeouts that are due now.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// The number of timeouts this pass claimed, which is a diagnostic rather than an assertion
    /// target. Publishing a timeout's event usually advances the saga past the step that scheduled
    /// the timer, and advancing cancels that step's timers, so the row can be gone before the claim
    /// lands and the pass reports it as unclaimed even though the reminder did its work. Assert on
    /// the effect on the saga instead.
    /// </returns>
    Task<int> DrainDueAsync(CancellationToken cancellationToken = default);
}
