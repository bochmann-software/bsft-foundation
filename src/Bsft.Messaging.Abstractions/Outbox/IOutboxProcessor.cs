namespace Bsft.Messaging.Outbox;

/// <summary>
/// Runs a single drain pass over the transactional outbox on demand, instead of waiting for the
/// dispatcher's polling loop to come round.
/// </summary>
/// <remarks>
/// <para>
/// This is the supported seam for a test that has to observe the effect of a published integration
/// event: stage the change, commit it, drain, and assert. It is registered on the same instance the
/// host runs as a background service, so draining exercises the real lease, retry and parking policy
/// rather than a reimplementation that can drift.
/// </para>
/// <para>
/// Before 1.3.6.1 a host test could only shorten <c>Bsft:Messaging:Outbox:PollInterval</c> and poll
/// until the effect appeared, which turns a deterministic assertion into a timeout.
/// </para>
/// </remarks>
public interface IOutboxProcessor
{
    /// <summary>Leases one batch of pending messages and publishes them.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// The number of messages published and marked processed in this pass. A pass drains at most one
    /// batch, so a caller draining a backlog repeats until the count is zero.
    /// </returns>
    Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default);
}
