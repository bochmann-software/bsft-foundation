namespace Bsft.Scheduling.Tests.Fakes;

/// <summary>A job that simply records that it ran.</summary>
internal sealed class RecordingJob : IJob
{
    private readonly JobLog _log;

    public RecordingJob(JobLog log) => _log = log;

    public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default)
    {
        _log.Record(context);
        return Task.CompletedTask;
    }
}
