namespace Bsft.Messaging.Inbox;

/// <summary>
/// The default <see cref="IInboxStore"/>: it keeps the record of processed events in memory, in the
/// process-wide <see cref="InMemoryInboxLedger"/>. One instance serves one delivery, which is what lets
/// <see cref="CommitAsync"/> and <see cref="AbandonAsync"/> know which claim they are finishing.
/// </summary>
/// <remarks>
/// <para>
/// The ledger records completed deliveries only. Nothing is written while the handlers run, so a
/// delivery that fails leaves no trace and the redelivery is claimed as if it were new — which is the
/// behaviour that makes a failure cost a retry rather than a lost event.
/// </para>
/// <para>
/// The consequence is one window this store does not close: two deliveries of the same event running
/// at the same time both see it unprocessed and both reach the handlers. The consumer host takes one
/// message at a time, so that needs two hosts sharing this ledger under one consumer name, which is a
/// misconfiguration on its own. <c>EFCoreInboxStore</c> closes it properly, with a unique index that
/// makes the database refuse the second mark.
/// </para>
/// </remarks>
internal sealed class InMemoryInboxStore : IInboxStore
{
    private readonly InMemoryInboxLedger _ledger;
    private (Guid EventId, string Consumer)? _claim;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryInboxStore"/> class.
    /// </summary>
    /// <param name="ledger">The process-wide record of completed deliveries.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ledger"/> is null.</exception>
    public InMemoryInboxStore(InMemoryInboxLedger ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    /// <inheritdoc />
    public Task<bool> TryBeginAsync(
        IntegrationEventMessage message, string consumer, DateTimeOffset receivedAt, CancellationToken cancellationToken = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        if (consumer is null)
            throw new ArgumentNullException(nameof(consumer));

        if (_ledger.Contains(message.EventId, consumer))
            return Task.FromResult(false);

        _claim = (message.EventId, consumer);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when no claim is being held, which means the caller committed a delivery it never began.
    /// </exception>
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        var claim = _claim ?? throw new InvalidOperationException(
            "There is no inbox claim to commit; TryBeginAsync must have returned true before CommitAsync is called.");

        _ledger.Add(claim.EventId, claim.Consumer);
        _claim = null;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AbandonAsync(CancellationToken cancellationToken = default)
    {
        // Nothing was written when the claim was taken, so releasing it is only forgetting it. It is
        // deliberately tolerant of there being no claim: the host abandons on any failure, including
        // one raised before or by TryBeginAsync itself.
        _claim = null;

        return Task.CompletedTask;
    }
}
