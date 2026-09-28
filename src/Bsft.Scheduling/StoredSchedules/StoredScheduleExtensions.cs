namespace Bsft.Scheduling.StoredSchedules;

/// <summary>
/// Internal helpers shared by the stored-schedule store implementations.
/// </summary>
internal static class StoredScheduleExtensions
{
    /// <summary>
    /// Creates a detached copy of a schedule.
    /// </summary>
    /// <remarks>
    /// Both stores hand out copies rather than the instances they hold, matching what a
    /// database-backed store necessarily does. Sharing the copy here keeps the two from drifting apart
    /// when a field is added to <see cref="StoredSchedule"/>.
    /// </remarks>
    /// <param name="schedule">The schedule to copy.</param>
    /// <returns>A new instance carrying the same values.</returns>
    internal static StoredSchedule Copy(this StoredSchedule schedule) => new()
    {
        Id = schedule.Id,
        TenantId = schedule.TenantId,
        JobKey = schedule.JobKey,
        Schedule = schedule.Schedule,
        Payload = schedule.Payload,
        Enabled = schedule.Enabled,
        NextRunAt = schedule.NextRunAt,
        LastRunAt = schedule.LastRunAt,
        CreatedAt = schedule.CreatedAt,
        UpdatedAt = schedule.UpdatedAt,
        ConcurrencyToken = schedule.ConcurrencyToken,
    };
}
