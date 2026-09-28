
namespace Bsft.Messaging.Inbox;

/// <summary>
/// Storage contract for the inbox: the record of which events this consumer has already processed.
/// It turns idempotency from something every handler has to remember into something the pipeline does,
/// by refusing a redelivered event before the handler ever sees it.
/// </summary>
/// <remarks>
/// <para>
/// It is the mirror of <see cref="Outbox.IOutboxStore"/>. The outbox guarantees an event is published at least
/// once; the inbox is what stops "at least once" from meaning "handled twice". The record is keyed by
/// the event's identity <em>and</em> the consumer's, because two consumers of the same event are
/// independent of each other and may well share one database.
/// </para>
/// <para>
/// A store is resolved from the delivery's own service scope, one instance per delivery, and the two
/// halves of a delivery are two calls on that instance: <see cref="TryBeginAsync"/> before the handlers
/// run and <see cref="CommitAsync"/> after they have all succeeded. Splitting it is what allows the
/// only implementation that can be atomic to be atomic — an implementation sharing the consuming
/// application's <c>DbContext</c> stages the mark on that context in the first call and lets the second
/// call's <c>SaveChanges</c> commit it together with everything the handlers wrote.
/// </para>
/// </remarks>
public interface IInboxStore
{
    /// <summary>
    /// Claims the event for this consumer if no completed delivery of it has been recorded yet.
    /// </summary>
    /// <param name="message">
    /// The message that was received. Only its <see cref="IntegrationEventMessage.EventId"/> takes part
    /// in the key; the rest is available to a store that wants to record what it refused or accepted.
    /// </param>
    /// <param name="consumer">The identity of the consumer, distinguishing independent consumers of the same event.</param>
    /// <param name="receivedAt">The UTC instant at which the message was received.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// <see langword="true"/> when the event is new to this consumer and the handlers must run;
    /// <see langword="false"/> when it has already been processed and the message is a redelivery to be
    /// acknowledged without handling.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    Task<bool> TryBeginAsync(
        IntegrationEventMessage message, string consumer, DateTimeOffset receivedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the claim as a completed delivery, so the event is refused from here on.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the record is durable.</returns>
    /// <remarks>
    /// Called only after every handler succeeded. An implementation sharing the application's unit of
    /// work commits the handlers' writes in the same breath, which is what makes the mark and the work
    /// it stands for impossible to disagree.
    /// </remarks>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the claim back after a failed delivery, so the event is claimable again when it is
    /// redelivered.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the claim has been released.</returns>
    /// <remarks>
    /// A claim that is never released and never committed would make the event look processed to the
    /// next delivery, so a failure the transport is willing to retry would silently become an event
    /// nobody ever handled. Releasing must therefore be safe to do after any failure, including one
    /// raised by <see cref="CommitAsync"/> itself.
    /// <para>
    /// Releasing means the event is claimable again, not merely that the store stopped intending to
    /// write the mark. An implementation whose claim can already have become durable without
    /// <see cref="CommitAsync"/> — the shared unit of work makes that possible, because anything else
    /// saving that unit of work saves the staged mark with it — has to undo the durable record too.
    /// Dropping only the intent would leave the row that refuses the redelivery.
    /// </para>
    /// </remarks>
    Task AbandonAsync(CancellationToken cancellationToken = default);
}
