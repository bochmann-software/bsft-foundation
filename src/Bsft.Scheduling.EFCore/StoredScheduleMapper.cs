using Bsft.Scheduling.StoredSchedules;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// Default <see cref="IStoredScheduleMapper"/>.
/// </summary>
internal sealed class StoredScheduleMapper : IStoredScheduleMapper
{
    /// <inheritdoc />
    public StoredScheduleEntity ToEntity(StoredSchedule schedule)
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        return new StoredScheduleEntity
        {
            TenantId = schedule.TenantId,
            JobKey = schedule.JobKey,
            CronExpression = schedule.Schedule.CronExpressionText,
            IntervalTicks = schedule.Schedule.Interval?.Ticks,
            Payload = schedule.Payload,
            Enabled = schedule.Enabled,
            NextRunAt = schedule.NextRunAt?.UtcDateTime,
            LastRunAt = schedule.LastRunAt?.UtcDateTime,
            CreatedAt = schedule.CreatedAt.UtcDateTime,
            UpdatedAt = schedule.UpdatedAt.UtcDateTime,
            ConcurrencyToken = schedule.ConcurrencyToken,
        };
    }

    /// <inheritdoc />
    public StoredSchedule ToSchedule(StoredScheduleEntity entity)
    {
        if (entity is null)
            throw new ArgumentNullException(nameof(entity));

        return new StoredSchedule
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            JobKey = entity.JobKey,
            Schedule = ToJobSchedule(entity),
            Payload = entity.Payload,
            Enabled = entity.Enabled,
            NextRunAt = entity.NextRunAt is { } next ? ToOffset(next) : null,
            LastRunAt = entity.LastRunAt is { } last ? ToOffset(last) : null,
            CreatedAt = ToOffset(entity.CreatedAt),
            UpdatedAt = ToOffset(entity.UpdatedAt),
            ConcurrencyToken = entity.ConcurrencyToken,
        };
    }

    /// <summary>
    /// Rebuilds the recurrence rule from whichever of the two persisted shapes the entity carries.
    /// </summary>
    private static JobSchedule ToJobSchedule(StoredScheduleEntity entity) =>
        entity.CronExpression is { } cron ? JobSchedule.Cron(cron) : JobSchedule.Every(TimeSpan.FromTicks(entity.IntervalTicks!.Value));

    /// <summary>
    /// Reinstates the UTC kind a provider may have dropped on the way out of the database, so the
    /// resulting offset is zero rather than the host's local one.
    /// </summary>
    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
