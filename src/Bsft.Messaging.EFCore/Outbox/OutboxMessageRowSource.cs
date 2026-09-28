using Bsft.Outbox.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// Where the integration-event outbox's rows live and how one is read: the two things the shared
/// lease protocol asks this package for.
/// </summary>
/// <remarks>
/// The set comes from the application's own <see cref="IOutboxDbContext"/>, which is the whole point
/// of keeping the outbox in that context: the state change and the event it raises are written by one
/// <c>SaveChanges</c> and cannot disagree.
/// </remarks>
internal sealed class OutboxMessageRowSource : IOutboxRowSource<OutboxMessageEntity, IntegrationEventMessage>
{
    /// <inheritdoc />
    public int FailureReasonMaxLength => OutboxMessageEntityConfiguration.FailureReasonMaxLength;

    /// <inheritdoc />
    public DbSet<OutboxMessageEntity> Rows(IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        return scopedServices.GetRequiredService<IOutboxDbContext>().OutboxMessages;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Nothing is re-encoded: the payload is the one the row was written with, so what a consumer
    /// reads is what the transaction committed.
    /// </remarks>
    public IntegrationEventMessage PayloadOf(OutboxMessageEntity row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new IntegrationEventMessage
        {
            EventId = row.EventId,
            EventName = row.EventTypeName,
            OccurredAt = new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)),
            CorrelationId = row.CorrelationId,
            Payload = row.PayloadJson,
        };
    }
}
