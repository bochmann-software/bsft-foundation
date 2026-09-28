using Bsft.Messaging.Outbox;
using Bsft.Outbox;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// This package's <see cref="IOutboxStore"/>, answered by the shared EF Core lease protocol.
/// </summary>
/// <remarks>
/// <see cref="IOutboxStore"/> is what a host sees and may replace, and it predates the shared
/// protocol, so it stays exactly as it was; the protocol underneath is
/// <see cref="Bsft.Outbox.EFCore.EFCoreOutboxStore{TEntity, TPayload}"/>, which the audit that
/// found this lease sound was about. Keeping one implementation of it is the point of the
/// arrangement: a second outbox is a second table rather than a second protocol.
/// </remarks>
internal sealed class EFCoreOutboxStore : IOutboxStore
{
    private readonly IOutboxStore<IntegrationEventMessage> _rows;

    /// <summary>Initializes a new instance of the <see cref="EFCoreOutboxStore"/> class.</summary>
    /// <param name="rows">The shared lease protocol over this package's outbox table.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rows"/> is null.</exception>
    public EFCoreOutboxStore(IOutboxStore<IntegrationEventMessage> rows)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeasedOutboxMessage>> LeaseAsync(
        DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken cancellationToken = default)
    {
        var leased = await _rows.LeaseAsync(now, leaseDuration, batchSize, cancellationToken).ConfigureAwait(false);
        var messages = new List<LeasedOutboxMessage>(leased.Count);

        foreach (var row in leased)
        {
            messages.Add(new LeasedOutboxMessage
            {
                Id = row.Id,
                EventId = row.Payload.EventId,
                EventTypeName = row.Payload.EventName,
                PayloadJson = row.Payload.Payload,
                OccurredAt = row.Payload.OccurredAt,
                CorrelationId = row.Payload.CorrelationId,
                AttemptCount = row.AttemptCount,
                LeaseToken = row.LeaseToken,
            });
        }

        return messages;
    }

    /// <inheritdoc />
    public Task<bool> TryMarkProcessedAsync(
        long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken cancellationToken = default)
        => _rows.TryMarkProcessedAsync(id, leaseToken, processedAt, cancellationToken);

    /// <inheritdoc />
    public Task AbandonAsync(long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
        => _rows.AbandonAsync(id, leaseToken, nextAttemptAt, cancellationToken);

    /// <inheritdoc />
    public Task DeadLetterAsync(
        long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken cancellationToken = default)
        => _rows.DeadLetterAsync(id, leaseToken, deadLetteredAt, reason, cancellationToken);
}
