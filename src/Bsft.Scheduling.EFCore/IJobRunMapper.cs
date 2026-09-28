namespace Bsft.Scheduling.EFCore;

/// <summary>
/// Translates between the scheduler's <see cref="JobRun"/> and the persisted
/// <see cref="JobRunEntity"/>. It is a concern of its own: the store decides which rows to read and
/// write, this decides what a row means.
/// </summary>
internal interface IJobRunMapper
{
    /// <summary>Builds the row that persists a run.</summary>
    /// <param name="run">The run to persist.</param>
    /// <returns>The entity carrying the run's state.</returns>
    JobRunEntity ToEntity(JobRun run);

    /// <summary>Reads a persisted row back into a run.</summary>
    /// <param name="entity">The stored row.</param>
    /// <returns>The run the row describes.</returns>
    JobRun ToRun(JobRunEntity entity);
}
