using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// EF Core entity type configuration for <see cref="StoredScheduleEntity"/>.
/// </summary>
public sealed class StoredScheduleEntityConfiguration : IEntityTypeConfiguration<StoredScheduleEntity>
{
    /// <summary>Matches the longest assembly-qualified type name a default job name can produce, same as <c>JobRunEntity.JobName</c>.</summary>
    private const int JobKeyMaxLength = 500;

    /// <summary>Ample for a tenant identifier.</summary>
    private const int TenantIdMaxLength = 200;

    /// <summary>Comfortably longer than any five-field cron expression a host would write.</summary>
    private const int CronExpressionMaxLength = 200;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoredScheduleEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StoredSchedules");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).HasMaxLength(TenantIdMaxLength);
        builder.Property(e => e.JobKey).IsRequired().HasMaxLength(JobKeyMaxLength);
        builder.Property(e => e.CronExpression).HasMaxLength(CronExpressionMaxLength);

        // The seeder polls this shape on every pass: enabled rows whose next occurrence is due, oldest
        // first. The id closes the key for the same reason JobRunEntity's poll index does — a stable
        // tie-breaker lets a batch stream off the index instead of sorting a backlog in full.
        builder.HasIndex(e => new { e.Enabled, e.NextRunAt, e.Id });

        // The management surface lists a tenant's own schedules; without this index that read scans
        // the whole catalog.
        builder.HasIndex(e => e.TenantId);

        // A recurrence rule is a cron expression or an interval, never both and never neither. The
        // mapper reads the row back on that promise, so a row that broke it would fault the seeder on
        // every pass. Written as a sum of cases rather than a comparison of two predicates, because
        // that is the spelling every relational provider here accepts.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_StoredSchedules_OneRecurrenceRule",
            "(CASE WHEN CronExpression IS NULL THEN 0 ELSE 1 END + " +
            "CASE WHEN IntervalTicks IS NULL THEN 0 ELSE 1 END) = 1"));

        // Provider-agnostic optimistic concurrency token: it turns the advance race between two
        // instances into a detectable conflict instead of a duplicate run.
        builder.Property(e => e.ConcurrencyToken).IsConcurrencyToken();
    }
}
