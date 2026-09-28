using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// EF Core entity type configuration for <see cref="SagaStateEntity"/>.
/// </summary>
public sealed class SagaStateEntityConfiguration : IEntityTypeConfiguration<SagaStateEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SagaStateEntity> builder)
    {
        builder.ToTable("SagaStates");

        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.SagaType, e.CorrelationKey })
            .IsUnique();

        // The lengths are what makes the index above creatable: an unbounded string maps to
        // nvarchar(max) on SQL Server, which that provider refuses as an index key column outright.
        builder.Property(e => e.SagaType).IsRequired().HasMaxLength(SagaColumnLengths.SagaType);
        builder.Property(e => e.CorrelationKey).IsRequired().HasMaxLength(SagaColumnLengths.CorrelationKey);
        builder.Property(e => e.StateTypeName).IsRequired().HasMaxLength(SagaColumnLengths.TypeName);
        builder.Property(e => e.StateJson).IsRequired();

        // Provider-agnostic optimistic concurrency token (managed by EFCoreSagaStore on each save).
        builder.Property(e => e.ConcurrencyToken).IsConcurrencyToken();
    }
}
