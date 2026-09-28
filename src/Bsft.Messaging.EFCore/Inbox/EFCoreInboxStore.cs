using Bsft.Messaging.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bsft.Messaging.EFCore.Inbox;

/// <summary>
/// An <see cref="IInboxStore"/> backed by EF Core. Unlike the outbox store it is scoped and takes the
/// application's <see cref="IInboxDbContext"/> directly, because that is the point of it: the mark it
/// stages and the changes the handlers make are on one context, so one <c>SaveChanges</c> commits both.
/// </summary>
/// <remarks>
/// <para>
/// The claim is staged, not written. <see cref="TryBeginAsync"/> adds the row to the context and
/// returns; nothing reaches the database until <see cref="CommitAsync"/> saves, by which time the
/// handlers have added their own changes to the same context. Either the whole delivery lands or none
/// of it does, and there is no instant at which the event counts as processed but its effects are
/// missing.
/// </para>
/// <para>
/// A handler that calls <c>SaveChanges</c> itself commits the mark with its own writes at that moment
/// instead, which is equally atomic for everything it had written by then. What no inbox can make
/// atomic is a handler that saves twice, or a later handler that fails, after that commit; the work
/// already written stays written. What this store does guarantee is that such a delivery is still
/// <em>retried</em>: <see cref="AbandonAsync"/> deletes a mark a handler already committed, so the
/// redelivery reaches the handlers instead of being refused as a duplicate.
/// </para>
/// </remarks>
internal sealed class EFCoreInboxStore : IInboxStore
{
    private readonly IInboxDbContext _db;
    private readonly ILogger<EFCoreInboxStore> _logger;
    private InboxMessageEntity? _claim;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreInboxStore"/> class.
    /// </summary>
    /// <param name="db">The application's DbContext, scoped to this delivery.</param>
    /// <param name="logger">Logger used to report a claim that had to be deleted rather than detached.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public EFCoreInboxStore(IInboxDbContext db, ILogger<EFCoreInboxStore> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> TryBeginAsync(
        IntegrationEventMessage message, string consumer, DateTimeOffset receivedAt, CancellationToken cancellationToken = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        if (consumer is null)
            throw new ArgumentNullException(nameof(consumer));

        var eventId = message.EventId;

        // AsNoTracking because nothing is loaded to be changed: an existing record is only ever read
        // to be counted, and tracking it would put a row this delivery must not touch into the same
        // change tracker the handlers are about to save.
        var alreadyProcessed = await _db.InboxMessages
            .AsNoTracking()
            .AnyAsync(e => e.EventId == eventId && e.Consumer == consumer, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyProcessed)
            return false;

        // The read above cannot settle a race with another delivery of the same event: both would find
        // nothing. The unique index on (Consumer, EventId) settles it instead, by failing the second
        // commit — which the consumer treats as a failed delivery, retries, and then recognises here as
        // the duplicate it has become.
        _claim = new InboxMessageEntity
        {
            EventId = eventId,
            Consumer = consumer,
            EventName = message.EventName,
            ReceivedAt = receivedAt.UtcDateTime,
        };

        _db.InboxMessages.Add(_claim);

        return true;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_claim is null)
            throw new InvalidOperationException(
                "There is no inbox claim to commit; TryBeginAsync must have returned true before CommitAsync is called.");

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _claim = null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A staged claim is detached and a committed one is deleted, which are two different operations
    /// because a claim can have been committed without this store ever saving: a handler that calls
    /// <c>SaveChanges</c> writes the staged mark along with its own changes. Scheduling a delete would
    /// not undo that — the delivery scope disposes the context straight after this call, so nothing
    /// would ever save it — and the row left behind refuses the redelivery as a duplicate. That is a
    /// failure the consumer retries turning into an event nobody handles, so it is undone here instead.
    /// </remarks>
    public async Task AbandonAsync(CancellationToken cancellationToken = default)
    {
        if (_claim is null)
            return;

        var claim = _claim;
        _claim = null;

        // Added is the state of a claim no save has reached yet, and Removing such an entity detaches
        // it rather than scheduling a delete, so the claim simply stops existing and no later save can
        // carry it in by accident. It is also what a claim whose commit failed still is — a commit
        // refused by the unique index leaves the states unaccepted — and that case must take this
        // branch: the row that exists then belongs to the delivery that won the race, and deleting it
        // would drop somebody else's completed mark.
        if (_db.InboxMessages.Entry(claim).State == EntityState.Added)
        {
            _db.InboxMessages.Remove(claim);

            return;
        }

        await DeleteCommittedClaimAsync(claim, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a claim a handler's own <c>SaveChanges</c> already committed, so the redelivery of the
    /// failed delivery is not refused as a duplicate.
    /// </summary>
    /// <param name="claim">The claim to delete, identified by the consumer and event it was made for.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the delete has been attempted.</returns>
    private async Task DeleteCommittedClaimAsync(InboxMessageEntity claim, CancellationToken cancellationToken)
    {
        var eventId = claim.EventId;
        var consumer = claim.Consumer;

        try
        {
            // Through the context the mark was committed on rather than a connection of its own. A
            // handler may still hold an open transaction: on that context the delete joins it and is
            // rolled back with everything else, which is correct because the mark was never committed
            // either — while a second connection would instead block on the locks that transaction
            // holds over the very row it is trying to delete.
            //
            // The predicate names one row and can only name ours. The unique index on
            // (Consumer, EventId) means at most one row exists for this pair, and this delivery is what
            // wrote it: any concurrent delivery of the same event to the same consumer would have had
            // its own commit refused by that index rather than replacing this row.
            var deleted = await _db.InboxMessages
                .Where(e => e.EventId == eventId && e.Consumer == consumer)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
                _logger.LogWarning(
                    "The inbox mark for event {EventId} of consumer {Consumer} had been committed by a handler's own SaveChanges before the delivery failed, and has been deleted so the redelivery is handled. Anything that handler had written stays written, so the redelivery has to tolerate seeing it.",
                    eventId,
                    consumer);
        }
        catch (Exception failure)
        {
            // Deliberately broad, and deliberately not rethrown. This runs in the failure path of a
            // delivery that is already unwinding, and the caller abandons in a finally: rethrowing
            // would replace the handler failure that explains the delivery with a database error that
            // explains only the cleanup, and would change nothing else — the delivery fails either way.
            // Swallowing it is not the same as hiding it, because the consequence is unrecoverable
            // without an operator and is spelled out here rather than left to be inferred.
            _logger.LogError(
                failure,
                "The inbox mark for event {EventId} of consumer {Consumer} was committed by a handler's own SaveChanges and could not be deleted after the delivery failed. The row is still there, so every redelivery of this event is refused as a duplicate and the failed handlers never run again. Delete that row by hand to release the event.",
                eventId,
                consumer);
        }
    }
}
