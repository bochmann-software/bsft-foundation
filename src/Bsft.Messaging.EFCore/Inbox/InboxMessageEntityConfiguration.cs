using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Messaging.EFCore.Inbox;

/// <summary>
/// EF Core entity type configuration for <see cref="InboxMessageEntity"/>.
/// </summary>
public sealed class InboxMessageEntityConfiguration : IEntityTypeConfiguration<InboxMessageEntity>
{
    /// <summary>The column length of the consumer identity.</summary>
    public const int ConsumerMaxLength = 200;

    /// <summary>The column length of the registered event name.</summary>
    public const int EventNameMaxLength = 500;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxMessageEntity> builder)
    {
        builder.ToTable("InboxMessages");

        builder.HasKey(e => e.Id);

        // A length is mandatory on the indexed columns, not cosmetic: an unbounded string maps to
        // nvarchar(max) on SQL Server, which is rejected outright as an index key column.
        builder.Property(e => e.Consumer).IsRequired().HasMaxLength(ConsumerMaxLength);
        builder.Property(e => e.EventName).IsRequired().HasMaxLength(EventNameMaxLength);

        // Unique, and that is the whole point of the table. The lookup that precedes a delivery is a
        // read, so two deliveries of the same event racing each other can both find nothing; this index
        // is what makes the database refuse the second insert instead, turning the race into one failed
        // transaction that is retried and then recognised as the duplicate it is.
        //
        // Consumer leads, because every query is for one consumer: the uniqueness check, and an
        // operator asking what a given consumer has processed. Leading with EventId would serve
        // neither without scanning.
        builder.HasIndex(e => new { e.Consumer, e.EventId }).IsUnique();
    }
}
