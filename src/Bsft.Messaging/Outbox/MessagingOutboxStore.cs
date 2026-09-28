using Bsft.Outbox;

namespace Bsft.Messaging.Outbox;

/// <summary>
/// Presents this package's own outbox store — the one a host may have implemented itself — as the
/// store the drain kernel leases from.
/// </summary>
/// <remarks>
/// <see cref="IOutboxStore"/> is public and predates the kernel, so a host implementation of it has to
/// keep working. Mapping here rather than widening that contract is what lets the kernel own the
/// protocol without any host having to be recompiled against a generic one.
/// </remarks>
internal sealed class MessagingOutboxStore : IOutboxStore<IntegrationEventMessage>
{
    private readonly IOutboxStore _store;

    /// <summary>Initializes a new instance of the <see cref="MessagingOutboxStore"/> class.</summary>
    /// <param name="store">The store this package publishes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
    public MessagingOutboxStore(IOutboxStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeasedOutboxRow<IntegrationEventMessage>>> LeaseAsync(
        DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken cancellationToken = default)
    {
        var leased = await _store.LeaseAsync(now, leaseDuration, batchSize, cancellationToken).ConfigureAwait(false);
        var rows = new List<LeasedOutboxRow<IntegrationEventMessage>>(leased.Count);

        foreach (var message in leased)
        {
            rows.Add(new LeasedOutboxRow<IntegrationEventMessage>(
                message.Id, ToWireMessage(message), message.AttemptCount, message.LeaseToken));
        }

        return rows;
    }

    /// <inheritdoc />
    public Task<bool> TryMarkProcessedAsync(
        long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken cancellationToken = default)
        => _store.TryMarkProcessedAsync(id, leaseToken, processedAt, cancellationToken);

    /// <inheritdoc />
    public Task AbandonAsync(long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
        => _store.AbandonAsync(id, leaseToken, nextAttemptAt, cancellationToken);

    /// <inheritdoc />
    public Task DeadLetterAsync(
        long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken cancellationToken = default)
        => _store.DeadLetterAsync(id, leaseToken, deadLetteredAt, reason, cancellationToken);

    /// <summary>
    /// Wraps a stored row in the message that goes on the wire. Nothing is re-encoded: the payload is
    /// the one the row was written with, so what a consumer reads is what the transaction committed.
    /// </summary>
    private static IntegrationEventMessage ToWireMessage(LeasedOutboxMessage message) => new()
    {
        EventId = message.EventId,
        EventName = message.EventTypeName,
        OccurredAt = message.OccurredAt,
        CorrelationId = message.CorrelationId,
        Payload = message.PayloadJson,
    };
}
