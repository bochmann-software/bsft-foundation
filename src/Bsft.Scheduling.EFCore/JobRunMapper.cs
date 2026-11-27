using System.Text.Json;

namespace Bsft.Scheduling.EFCore;

/// <summary>
/// Default <see cref="IJobRunMapper"/>.
/// </summary>
internal sealed class JobRunMapper : IJobRunMapper
{
    /// <inheritdoc />
    public JobRunEntity ToEntity(JobRun run)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        return new JobRunEntity
        {
            JobName = run.JobName,
            DueAt = run.DueAt.UtcDateTime,
            Status = run.Status,
            IsRecurring = run.IsRecurring,
            Payload = run.Payload,
            CorrelationId = run.CorrelationId,
            AttemptCount = run.AttemptCount,
            LeaseOwner = run.LeaseOwner,
            LeaseExpiresAt = run.LeaseExpiresAt?.UtcDateTime,
            SettledAt = run.SettledAt?.UtcDateTime,
            ResultSchema = run.Result?.Schema,
            Result = ToResultJson(run.Result),
            ConcurrencyToken = run.ConcurrencyToken,
        };
    }

    /// <inheritdoc />
    public JobRun ToRun(JobRunEntity entity)
    {
        if (entity is null)
            throw new ArgumentNullException(nameof(entity));

        return new JobRun
        {
            Id = entity.Id,
            JobName = entity.JobName,
            DueAt = ToOffset(entity.DueAt),
            Status = entity.Status,
            IsRecurring = entity.IsRecurring,
            Payload = entity.Payload,
            CorrelationId = entity.CorrelationId,
            AttemptCount = entity.AttemptCount,
            LeaseOwner = entity.LeaseOwner,
            LeaseExpiresAt = entity.LeaseExpiresAt is { } lease ? ToOffset(lease) : null,
            SettledAt = entity.SettledAt is { } settled ? ToOffset(settled) : null,
            Result = ToResult(entity),
            ConcurrencyToken = entity.ConcurrencyToken,
        };
    }

    /// <inheritdoc />
    public string? ToResultJson(JobRunResult? result) => result?.Value.GetRawText();

    /// <inheritdoc />
    public JobRunResult? ToResult(JobRunEntity entity)
    {
        if (entity is null)
            throw new ArgumentNullException(nameof(entity));

        return entity.ResultSchema is { } schema && entity.Result is { } json
            ? new JobRunResult(schema, JsonSerializer.Deserialize<JsonElement>(json))
            : null;
    }

    /// <summary>
    /// Reinstates the UTC kind a provider may have dropped on the way out of the database, so the
    /// resulting offset is zero rather than the host's local one.
    /// </summary>
    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
