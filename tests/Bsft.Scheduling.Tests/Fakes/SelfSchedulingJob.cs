namespace Bsft.Scheduling.Tests.Fakes;

/// <summary>A recurring job that carries its own schedule.</summary>
internal sealed class SelfSchedulingJob : IRecurringJob
{
    private readonly JobLog _log;

    public SelfSchedulingJob(JobLog log) => _log = log;

    public JobSchedule Schedule => JobSchedule.Every(TimeSpan.FromMinutes(10));

    public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default)
    {
        _log.Record(context);
        return Task.CompletedTask;
    }
}
