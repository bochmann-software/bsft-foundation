using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Idempotency.EFCore;

/// <summary>
/// The mapping for the idempotency record table.
/// </summary>
public sealed class IdempotencyRecordEntityConfiguration : IEntityTypeConfiguration<IdempotencyRecordEntity>
{
    /// <summary>The column length of the caller digest, which is a 64-character SHA-256 in hexadecimal.</summary>
    public const int ScopeMaxLength = 64;

    /// <summary>The column length of the request digest, in the same spelling.</summary>
    public const int FingerprintMaxLength = 64;

    /// <summary>
    /// The column length of the key, which is
    /// <see cref="IdempotencyDefaults.MaxSupportedKeyLength"/> — the same number the options
    /// validator caps <c>MaxKeyLength</c> at, so a host cannot configure a key this table refuses.
    /// </summary>
    public const int KeyMaxLength = IdempotencyDefaults.MaxSupportedKeyLength;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public void Configure(EntityTypeBuilder<IdempotencyRecordEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("IdempotencyRecords");
        builder.HasKey(entity => entity.Id);

        // A length is mandatory on the indexed columns, not cosmetic: an unbounded string maps to
        // nvarchar(max) on SQL Server, which is rejected outright as an index key column.
        builder.Property(entity => entity.Scope).IsRequired().HasMaxLength(ScopeMaxLength);
        builder.Property(entity => entity.Key).IsRequired().HasMaxLength(KeyMaxLength);
        builder.Property(entity => entity.Fingerprint).IsRequired().HasMaxLength(FingerprintMaxLength);

        // Unique on the pair, and that is the whole point of the table. The lookup that precedes a
        // reservation is a read, so two concurrent requests under one key both find nothing; this
        // index is what makes the database refuse the second insert instead, which the store reads
        // back as the in-progress answer the loser of the race has to give.
        //
        // Unfiltered on purpose. A filtered index — the shape JobRunEntityConfiguration needs,
        // where a settled row must stop competing for its name — would be wrong here: the
        // uniqueness has to hold for a reserved row and a completed one alike, because a completed
        // record is exactly what a replay is looked up by. There is therefore no provider-specific
        // predicate to pass in, and no constructor argument to pass it through.
        //
        // The scope leads, so every query the store makes — all of them equalities on both columns —
        // rides the index, and one tenant's keys are physically adjacent rather than interleaved
        // with another's.
        builder.HasIndex(entity => new { entity.Scope, entity.Key }).IsUnique();

        // What the pruner sweeps on, and it is one column because the entity keeps one expiry: a
        // reservation still inside its lease is not older than now and is therefore never pruned,
        // which is the property that keeps a sweep from freeing a key a request is still using.
        builder.HasIndex(entity => entity.ExpiresAt);
    }
}
