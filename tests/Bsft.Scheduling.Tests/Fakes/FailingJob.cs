namespace Bsft.Scheduling.Tests.Fakes;

/// <summary>A job that records its run and then fails, to exercise the retry path.</summary>
internal sealed class FailingJob : IJob
{
    private readonly JobLog _log;

    public FailingJob(JobLog log) => _log = log;

    public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default)
    {
        _log.Record(context);
        throw new InvalidOperationException("the job failed");
    }
}
