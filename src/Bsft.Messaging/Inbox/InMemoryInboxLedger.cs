using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.Inbox;

/// <summary>
/// The process-wide record behind <see cref="InMemoryInboxStore"/>: the events each consumer has
/// finished with. It is a singleton, because the per-delivery stores that read and write it have to
/// agree with each other and with every other delivery in the process.
/// </summary>
/// <remarks>
/// <para>
/// It lives and dies with the process, so it is the right inbox for tests and for a deployment that
/// can afford to re-handle its backlog after a restart, and the wrong one for anything that cannot —
/// a restart forgets every event, and the first redelivery after it reaches the handlers again. Use
/// the EF Core store where that matters.
/// </para>
/// <para>
/// It is bounded at <see cref="ConsumerOptions.InboxLedgerCapacity"/> and drops the oldest record to
/// stay there. A process-wide structure that only ever grows is a leak whose size is set by throughput
/// rather than by anything going wrong, and one entry per completed delivery is enough to reach that.
/// The bound is the same trade the restart already is, only smaller: an event whose record has been
/// dropped is handled again if it is redelivered. Dropping the oldest is what keeps that theoretical —
/// a record is only doing work until the last redelivery of its event has been refused, and by the
/// time a hundred thousand later deliveries have been recorded, that has long happened.
/// </para>
/// </remarks>
public sealed class InMemoryInboxLedger
{
    private readonly int _capacity;

    // Only completed deliveries are recorded, so a delivery that failed leaves nothing behind and the
    // event is claimable again. The value is unused; the dictionary is a concurrent set.
    private readonly ConcurrentDictionary<(Guid EventId, string Consumer), byte> _processed = new();

    // The order the records were written in, which the dictionary does not keep and the eviction needs:
    // it holds exactly the keys the dictionary holds, because only an add that actually inserted
    // appends here. A record evicted and then written again is simply appended a second time, as the
    // newest record it now is.
    private readonly ConcurrentQueue<(Guid EventId, string Consumer)> _order = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryInboxLedger"/> class.
    /// </summary>
    /// <param name="options">The messaging options carrying the ledger's capacity.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="ConsumerOptions.InboxLedgerCapacity"/> is below one.
    /// </exception>
    /// <remarks>
    /// The capacity is re-checked here although the options validator already rejects it at startup:
    /// the ledger can be constructed without the container, and a capacity below one would leave a
    /// ledger that evicts every record it takes — a duplicate guard that silently refuses nothing.
    /// </remarks>
    public InMemoryInboxLedger(IOptions<BsftMessagingOptions> options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var capacity = options.Value.Consumer.InboxLedgerCapacity;

        if (capacity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(options), capacity, "The inbox ledger capacity must be at least 1.");

        _capacity = capacity;
    }

    /// <summary>
    /// Gets the number of completed deliveries recorded so far, up to
    /// <see cref="ConsumerOptions.InboxLedgerCapacity"/>.
    /// </summary>
    public int Count => _processed.Count;

    /// <summary>
    /// Determines whether the consumer has already completed a delivery of the event.
    /// </summary>
    /// <param name="eventId">The identity of the integration event.</param>
    /// <param name="consumer">The identity of the consumer.</param>
    /// <returns><see langword="true"/> when a completed delivery is on record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="consumer"/> is null.</exception>
    public bool Contains(Guid eventId, string consumer)
    {
        if (consumer is null)
            throw new ArgumentNullException(nameof(consumer));

        return _processed.ContainsKey((eventId, consumer));
    }

    /// <summary>
    /// Records a completed delivery, so the event is refused from here on — until the record is the
    /// oldest of <see cref="ConsumerOptions.InboxLedgerCapacity"/> and a newer one takes its place.
    /// </summary>
    /// <param name="eventId">The identity of the integration event.</param>
    /// <param name="consumer">The identity of the consumer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="consumer"/> is null.</exception>
    public void Add(Guid eventId, string consumer)
    {
        if (consumer is null)
            throw new ArgumentNullException(nameof(consumer));

        // Recording the same delivery twice must not take a second place in the order, or a redelivery
        // that was correctly refused would still push a working record out of the ledger.
        if (!_processed.TryAdd((eventId, consumer), 0))
            return;

        _order.Enqueue((eventId, consumer));

        Evict();
    }

    /// <summary>
    /// Drops the oldest records until the ledger is back within its capacity.
    /// </summary>
    /// <remarks>
    /// Oldest first, never by chance: the record of a recent event is the one a redelivery still in
    /// flight will be refused by, and dropping that instead is how a bound would reopen the duplicate
    /// window it was supposed to be invisible to. Concurrent adds can evict one record more than they
    /// strictly had to, because each reads the count before the other has finished; that costs the
    /// oldest record a little early and never a recent one.
    /// </remarks>
    private void Evict()
    {
        while (_processed.Count > _capacity && _order.TryDequeue(out var oldest))
            _processed.TryRemove(oldest, out _);
    }
}
