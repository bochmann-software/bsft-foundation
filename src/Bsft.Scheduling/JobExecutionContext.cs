namespace Bsft.Scheduling;

/// <summary>
/// Describes the single execution of a job that is handed to <see cref="Bsft.Scheduling.IJob.ExecuteAsync"/>.
/// </summary>
/// <param name="JobId">The store-assigned identity of this run.</param>
/// <param name="JobName">The registered name of the job.</param>
/// <param name="ScheduledFor">The instant the run was due, which is not the instant it started.</param>
/// <param name="StartedAt">The instant the dispatcher began this attempt.</param>
/// <param name="AttemptNumber">The one-based number of this attempt; greater than one after a retry.</param>
/// <param name="CorrelationId">
/// The correlation id captured when the run was enqueued, restored for the duration of the execution.
/// </param>
/// <param name="Payload">
/// The opaque payload supplied at enqueue time, or <c>null</c> for a recurring job. The scheduler
/// never interprets it — a caller that needs structure serializes and deserializes its own.
/// </param>
public sealed record JobExecutionContext(
    long JobId,
    string JobName,
    DateTimeOffset ScheduledFor,
    DateTimeOffset StartedAt,
    int AttemptNumber,
    string? CorrelationId,
    string? Payload);
