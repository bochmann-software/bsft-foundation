using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// EF Core entity type configuration for <see cref="SagaTimeoutEntity"/>.
/// </summary>
public sealed class SagaTimeoutEntityConfiguration : IEntityTypeConfiguration<SagaTimeoutEntity>
{
    /// <summary>The pending-row filter for SQL Server and SQLite, whose identifier quoting matches.</summary>
    public const string DefaultPendingFilter = "[DispatchedAt] IS NULL";

    /// <summary>Column length of the timeout name, which is part of the unique pending-name index.</summary>
    private const int NameMaxLength = 100;

    /// <summary>
    /// Column width of the recorded dead-letter reason.
    /// </summary>
    public const int DeadLetterReasonMaxLength = 1000;

    /// <summary>
    /// Cuts a dead-letter reason to the column width without splitting a surrogate pair. It lives with
    /// the width it enforces rather than with the store that calls it: an exception message has no
    /// bound, and a reason cut short beats the very write that records why a timer failed being
    /// rejected. A cut through a surrogate pair leaves an unpaired surrogate, which a provider that
    /// validates its text encoding refuses.
    /// </summary>
    /// <param name="reason">The reason to fit into the column.</param>
    /// <returns>The reason, shortened to <see cref="DeadLetterReasonMaxLength"/> where necessary.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reason"/> is null.</exception>
    public static string FitReason(string reason)
    {
        if (reason is null)
            throw new ArgumentNullException(nameof(reason));

        if (reason.Length <= DeadLetterReasonMaxLength)
            return reason;

        var length = char.IsHighSurrogate(reason[DeadLetterReasonMaxLength - 1])
            ? DeadLetterReasonMaxLength - 1
            : DeadLetterReasonMaxLength;

        return reason[..length];
    }

    private readonly string? _pendingFilter;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaTimeoutEntityConfiguration"/> class with the
    /// default pending filter.
    /// </summary>
    /// <remarks>
    /// Declared explicitly rather than left to the optional parameter below, because EF Core's
    /// <c>ApplyConfigurationsFromAssembly</c> looks the constructor up with
    /// <c>Type.GetConstructor(Type.EmptyTypes)</c>. That match is by exact signature and ignores
    /// default values, so a type offering only the all-optional constructor is skipped without a
    /// warning — leaving the entity on convention mapping and this file's indexes absent.
    /// </remarks>
    public SagaTimeoutEntityConfiguration()
        : this(DefaultPendingFilter)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaTimeoutEntityConfiguration"/> class.
    /// </summary>
    /// <param name="pendingFilter">
    /// The SQL predicate that selects pending rows for the unique name index. Identifier quoting is
    /// provider-specific, so a provider other than SQL Server or SQLite has to supply its own —
    /// PostgreSQL, for instance, needs <c>"DispatchedAt" IS NULL</c>. Passing <c>null</c> drops the
    /// filter and with it the uniqueness guarantee, leaving duplicate pending names possible.
    /// </param>
    public SagaTimeoutEntityConfiguration(string? pendingFilter)
    {
        _pendingFilter = pendingFilter;
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SagaTimeoutEntity> builder)
    {
        builder.ToTable("SagaTimeouts");

        builder.HasKey(e => e.Id);

        // Lengths are mandatory on the indexed columns, not cosmetic: an unbounded string maps to
        // nvarchar(max) on SQL Server, which is rejected outright as an index key column.
        builder.Property(e => e.SagaType).IsRequired().HasMaxLength(SagaColumnLengths.SagaType);
        builder.Property(e => e.CorrelationKey).IsRequired().HasMaxLength(SagaColumnLengths.CorrelationKey);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(NameMaxLength);
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.PayloadTypeName).IsRequired().HasMaxLength(SagaColumnLengths.TypeName);
        builder.Property(e => e.DeadLetterReason).HasMaxLength(DeadLetterReasonMaxLength);

        // The dispatcher polls for pending rows (DispatchedAt is null) ordered by DueAt; the
        // composite index covers both the filter and the ordering. Filtered for the same reason as
        // the name index: dispatched rows are never read through it, so leaving them in would grow
        // the index for the lifetime of the table.
        // With a filter the index only holds pending rows, so DispatchedAt is constant within it and
        // keying on it would be dead weight; without one it still has to discriminate.
        // The id closes either key because the poll orders by DueAt and then by Id: with the
        // tie-breaker in the index a batch streams off it, without one a backlog is sorted in full
        // before the first row comes back.
        if (_pendingFilter is not null)
            builder.HasIndex(e => new { e.DueAt, e.Id }).HasFilter(_pendingFilter);
        else
            builder.HasIndex(e => new { e.DispatchedAt, e.DueAt, e.Id });

        // Cancelling and re-scheduling address a timeout by its name within a saga instance, and the
        // name is unique among the *pending* timers of that instance. The filter matters: a dispatched
        // row keeps its name, so an unfiltered unique index would forbid ever scheduling that name
        // again. Two instances racing to schedule the same name collide here instead of both
        // inserting a row and firing the reminder twice.
        var nameIndex = builder.HasIndex(e => new { e.SagaType, e.CorrelationKey, e.Name });

        if (_pendingFilter is not null)
        {
            nameIndex.IsUnique().HasFilter(_pendingFilter);

            // The operator read side lists every timeout of one instance, retired rows included, so
            // the filtered index above cannot answer it: that index holds pending rows only. Without
            // one of its own the lookup scans a table that only grows, because a dispatched or
            // dead-lettered row is never purged. The due order closes the key so the listing streams
            // off the index instead of being sorted after the fact. Only needed alongside the
            // filtered index: unfiltered, the name index leads with the same two columns and answers
            // the lookup from its prefix.
            builder.HasIndex(e => new { e.SagaType, e.CorrelationKey, e.DueAt, e.Id });
        }

        // Provider-agnostic optimistic concurrency token: it turns the claim race between two
        // application instances into a detectable conflict instead of a double dispatch.
        builder.Property(e => e.ConcurrencyToken).IsConcurrencyToken();
    }
}
