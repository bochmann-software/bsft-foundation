using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// EF Core entity type configuration for <see cref="OutboxMessageEntity"/>.
/// </summary>
public sealed class OutboxMessageEntityConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    /// <summary>The column length of the registered event type name.</summary>
    public const int EventTypeNameMaxLength = 500;

    /// <summary>
    /// The column length of the failure recorded for a retired message. It is
    /// <see cref="MessageReasonLimits.MaxReasonLength"/>, so a reason is cut at the same point here as
    /// on the transport that writes it onto a parked message.
    /// </summary>
    public const int FailureReasonMaxLength = MessageReasonLimits.MaxReasonLength;

    /// <summary>The column length of the correlation id carried with the message.</summary>
    public const int CorrelationIdMaxLength = 200;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(e => e.Id);

        // A length is mandatory on the indexed column, not cosmetic: an unbounded string maps to
        // nvarchar(max) on SQL Server, which is rejected outright as an index key column.
        builder.Property(e => e.EventTypeName).IsRequired().HasMaxLength(EventTypeNameMaxLength);
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.FailureReason).HasMaxLength(FailureReasonMaxLength);

        // Nullable and without a default: a row that carries no correlation id is a normal row, and so
        // is one written before this column existed. Both read back as null and dispatch unchanged.
        builder.Property(e => e.CorrelationId).HasMaxLength(CorrelationIdMaxLength);

        // The dispatcher claims rows that are neither published nor retired, whose lease has expired and
        // whose retry instant has passed, ordered by Id; the composite index covers the filter and the
        // ordering of that one hot query. NextAttemptAt sits with the other predicates and before Id,
        // which is the ordering column, so the index still answers the whole query.
        builder.HasIndex(e => new { e.ProcessedAt, e.DeadLetteredAt, e.LeasedUntil, e.NextAttemptAt, e.Id });

        // Not unique: at-least-once delivery may republish an event, and a consumer deduplicates on
        // this id. Enforcing uniqueness here would instead break the producer whose events legitimately
        // repeat an id, and it would turn a duplicate into a failed business transaction.
        builder.HasIndex(e => e.EventId);
    }
}
