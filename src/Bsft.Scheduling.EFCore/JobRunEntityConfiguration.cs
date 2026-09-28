using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// EF Core entity type configuration for <see cref="JobRunEntity"/>.
/// </summary>
public sealed class JobRunEntityConfiguration : IEntityTypeConfiguration<JobRunEntity>
{
    /// <summary>
    /// The pending-occurrence filter for SQL Server and SQLite, whose identifier quoting matches.
    /// <c>Status = 0</c> is <see cref="JobRunStatus.Pending"/>.
    /// </summary>
    public const string DefaultPendingFilter = "[IsRecurring] = 1 AND [Status] = 0";

    /// <summary>Matches the longest assembly-qualified type name a default job name can produce.</summary>
    private const int JobNameMaxLength = 500;

    /// <summary>Ample for a machine name plus a process suffix, and for a correlation id.</summary>
    private const int IdentifierMaxLength = 200;

    private readonly string? _pendingFilter;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunEntityConfiguration"/> class with the
    /// default pending filter.
    /// </summary>
    /// <remarks>
    /// Declared explicitly rather than left to the optional parameter below, because EF Core's
    /// <c>ApplyConfigurationsFromAssembly</c> looks the constructor up with
    /// <c>Type.GetConstructor(Type.EmptyTypes)</c>. That match is by exact signature and ignores
    /// default values, so a type offering only the all-optional constructor is skipped without a
    /// warning — leaving the entity on convention mapping and this file's indexes absent.
    /// </remarks>
    public JobRunEntityConfiguration()
        : this(DefaultPendingFilter)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunEntityConfiguration"/> class.
    /// </summary>
    /// <param name="pendingFilter">
    /// The SQL predicate that selects the rows the unique recurring-occurrence index covers.
    /// Identifier quoting is provider-specific, so a provider other than SQL Server or SQLite has to
    /// supply its own — PostgreSQL, for instance, needs <c>"IsRecurring" = true AND "Status" = 0</c>.
    /// Passing <c>null</c> leaves a plain index: no filter, and no uniqueness either, since that is the
    /// pair a provider without partial indexes cannot have — and with the uniqueness goes the
    /// guarantee that two instances seeding at the same moment cannot both create an occurrence of the
    /// same recurring job.
    /// </param>
    public JobRunEntityConfiguration(string? pendingFilter)
    {
        _pendingFilter = pendingFilter;
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JobRunEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("JobRuns");

        builder.HasKey(e => e.Id);

        // A length is mandatory on an indexed column, not cosmetic: an unbounded string maps to
        // nvarchar(max) on SQL Server, which is rejected outright as an index key column.
        builder.Property(e => e.JobName).IsRequired().HasMaxLength(JobNameMaxLength);
        builder.Property(e => e.LeaseOwner).HasMaxLength(IdentifierMaxLength);
        builder.Property(e => e.CorrelationId).HasMaxLength(IdentifierMaxLength);

        // The dispatcher polls by status and due date on every pass, so this index carries the
        // hottest query in the package. The id closes the key because the poll orders by DueAt and
        // then by Id: with the tie-breaker in the index a batch streams off it, without one a backlog
        // is sorted in full before the first row comes back.
        builder.HasIndex(e => new { e.Status, e.DueAt, e.Id });

        // The poll's second branch reclaims runs whose lease has lapsed. Without its own index that
        // branch can seek on the status but still needs a key lookup per candidate to read the lease
        // expiry, on a query that runs continuously. No id here: the poll orders by DueAt, which this
        // index does not lead with, so appending the tie-breaker would widen it without saving a sort.
        builder.HasIndex(e => new { e.Status, e.LeaseExpiresAt });

        // At most one *pending* occurrence per recurring job. This is the invariant the whole design
        // rests on: it is what makes seeding idempotent across instances. Filtered, because a settled
        // row keeps its name and an unfiltered unique index would forbid the job from ever being
        // scheduled again. Running rows are deliberately outside the filter — under
        // JobOverlapPolicy.Allow a successor is created while the current occurrence still runs, and
        // covering them would reject that write at the database.
        var occurrenceIndex = builder.HasIndex(e => e.JobName);

        if (_pendingFilter is not null)
            occurrenceIndex.IsUnique().HasFilter(_pendingFilter);

        // Provider-agnostic optimistic concurrency token: it turns the claim race between two
        // instances into a detectable conflict instead of a double execution.
        builder.Property(e => e.ConcurrencyToken).IsConcurrencyToken();
    }
}
