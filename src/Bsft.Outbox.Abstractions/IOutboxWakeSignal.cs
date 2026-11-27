namespace Bsft.Outbox;

/// <summary>
/// Tells a hosted drain worker that an outbox has new work, so it drains at once instead of waiting
/// for its next poll.
/// </summary>
/// <remarks>
/// <para>
/// The signal is a hint and never the guarantee. A worker that has one still falls back to its poll
/// interval, so a signal that is never raised, never delivered or lost with the connection it travelled
/// on costs the interval and nothing else. That is why an implementation may be best effort and why it
/// needs no acknowledgement.
/// </para>
/// <para>
/// A signal raised while nobody waits is not lost: the next wait completes at once. Several signals
/// raised before that wait collapse into one, because the worker drains the whole backlog per wake.
/// </para>
/// </remarks>
public interface IOutboxWakeSignal
{
    /// <summary>Completes when the outbox may have work, or immediately when a signal is already pending.</summary>
    /// <param name="cancellationToken">A token that abandons the wait without consuming a pending signal.</param>
    /// <returns>A task that completes when the outbox may have work.</returns>
    Task WaitAsync(CancellationToken cancellationToken = default);
}
