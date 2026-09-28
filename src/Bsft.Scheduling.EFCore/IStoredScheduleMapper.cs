using Bsft.Scheduling.StoredSchedules;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// Translates between <see cref="StoredSchedule"/> and its persisted <see cref="StoredScheduleEntity"/>.
/// </summary>
internal interface IStoredScheduleMapper
{
    /// <summary>Maps a schedule to the entity that is written to the database.</summary>
    /// <param name="schedule">The schedule to map.</param>
    /// <returns>The corresponding entity.</returns>
    StoredScheduleEntity ToEntity(StoredSchedule schedule);

    /// <summary>Maps a persisted entity back to a schedule.</summary>
    /// <param name="entity">The entity to map.</param>
    /// <returns>The corresponding schedule.</returns>
    StoredSchedule ToSchedule(StoredScheduleEntity entity);
}
