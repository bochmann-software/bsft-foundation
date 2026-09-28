using System.Diagnostics;
using System.Diagnostics.Metrics;
using Bsft.Instrumentation;

namespace Bsft.Scheduling.Diagnostics;

/// <summary>
/// Default <see cref="IJobTelemetry"/> implementation that emits through the vendor-neutral
/// "Bsft.Scheduling" instrumentation source.
/// </summary>
internal sealed class JobTelemetry : IJobTelemetry
{
    /// <summary>The activity source and meter name for scheduling telemetry.</summary>
    internal const string SourceName = "Bsft.Scheduling";

    private const string EnqueuedCounter = "bsft.job.enqueued";
    private const string ClaimedCounter = "bsft.job.claimed";
    private const string SkippedCounter = "bsft.job.skipped";
    private const string ExecutionDurationHistogram = "bsft.job.execution.duration";
    private const string JobNameTag = "job.name";

    private readonly IInstrumentationSource _source;
    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _claimed;
    private readonly Counter<long> _skipped;
    private readonly Histogram<double> _executionDuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobTelemetry"/> class.
    /// </summary>
    /// <param name="sources">The instrumentation source provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources"/> is null.</exception>
    public JobTelemetry(IInstrumentationSourceProvider sources)
    {
        if (sources is null)
            throw new ArgumentNullException(nameof(sources));

        _source = sources.Get(SourceName);
        _enqueued = _source.GetOrCreateCounter(EnqueuedCounter);
        _claimed = _source.GetOrCreateCounter(ClaimedCounter);
        _skipped = _source.GetOrCreateCounter(SkippedCounter);
        _executionDuration = _source.GetOrCreateHistogram(ExecutionDurationHistogram, "s");
    }

    /// <inheritdoc />
    public void JobEnqueued(string jobName) =>
        _enqueued.Add(1, new KeyValuePair<string, object?>(JobNameTag, jobName));

    /// <inheritdoc />
    public void JobClaimed(string jobName) =>
        _claimed.Add(1, new KeyValuePair<string, object?>(JobNameTag, jobName));

    /// <inheritdoc />
    public void JobSkipped(string jobName) =>
        _skipped.Add(1, new KeyValuePair<string, object?>(JobNameTag, jobName));

    /// <inheritdoc />
    public IJobExecutionTelemetryScope BeginExecution(string jobName, long jobId, int attemptNumber)
    {
        var activity = _source.StartActivity("job.execute");
        if (activity is not null)
        {
            activity.SetTag(JobNameTag, jobName);
            activity.SetTag("job.id", jobId);
            activity.SetTag("job.attempt", attemptNumber);
        }

        return new JobExecutionTelemetryScope(this, activity, jobName);
    }

    /// <summary>Records the duration of a finished execution with its outcome.</summary>
    /// <param name="jobName">The registered job name.</param>
    /// <param name="outcome">The outcome tag value.</param>
    /// <param name="seconds">The elapsed execution duration in seconds.</param>
    internal void RecordExecution(string jobName, string outcome, double seconds)
    {
        var tags = new TagList
        {
            { JobNameTag, jobName },
            { "outcome", outcome },
        };

        _executionDuration.Record(seconds, tags);
    }
}
