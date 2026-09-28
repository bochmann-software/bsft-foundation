using Bsft.Scheduling.Diagnostics;

namespace Bsft.Scheduling;

/// <summary>
/// Default <see cref="IJobScheduler"/>: validates the job against the registry, stamps the current
/// correlation id onto the run and writes it to the store.
/// </summary>
internal sealed class JobScheduler : IJobScheduler
{
    private readonly IJobStore _store;
    private readonly IJobRegistry _registry;
    private readonly IJobCorrelationAccessor _correlation;
    private readonly TimeProvider _timeProvider;
    private readonly IJobTelemetry _telemetry;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobScheduler"/> class.
    /// </summary>
    /// <param name="store">The store runs are written to.</param>
    /// <param name="registry">The registry a job name is validated against.</param>
    /// <param name="correlation">Supplies the correlation id stamped onto the run.</param>
    /// <param name="timeProvider">Supplies the instant a relative delay is measured from.</param>
    /// <param name="telemetry">Telemetry sink for the enqueue counter.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobScheduler(
        IJobStore store,
        IJobRegistry registry,
        IJobCorrelationAccessor correlation,
        TimeProvider timeProvider,
        IJobTelemetry telemetry)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <inheritdoc />
    public Task<long> EnqueueAsync(string jobName, TimeSpan delay, string? payload = null, CancellationToken cancellationToken = default)
    {
        if (jobName is null)
            throw new ArgumentNullException(nameof(jobName));

        return EnqueueAtAsync(jobName, _timeProvider.GetUtcNow() + delay, payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> EnqueueAtAsync(string jobName, DateTimeOffset dueAt, string? payload = null, CancellationToken cancellationToken = default)
    {
        if (jobName is null)
            throw new ArgumentNullException(nameof(jobName));

        // Rejecting an unknown name here rather than at execution time keeps a typo from becoming a
        // row that sits in the store forever and is only noticed when the work does not happen.
        if (_registry.Find(jobName) is null)
            throw JobNotRegisteredException.ForName(jobName);

        var run = new JobRun
        {
            JobName = jobName,
            DueAt = dueAt,
            Status = JobRunStatus.Pending,
            IsRecurring = false,
            Payload = payload,
            CorrelationId = _correlation.CorrelationId,
        };

        var id = await _store.EnqueueAsync(run, cancellationToken).ConfigureAwait(false);
        _telemetry.JobEnqueued(jobName);
        return id;
    }

    /// <inheritdoc />
    public Task<long> EnqueueAsync<TJob>(TimeSpan delay, string? payload = null, CancellationToken cancellationToken = default)
        where TJob : IJob
    {
        var registration = _registry.FindByType(typeof(TJob))
            ?? throw JobNotRegisteredException.ForType(typeof(TJob));

        return EnqueueAsync(registration.Name, delay, payload, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(long jobId, CancellationToken cancellationToken = default) => _store.CancelAsync(jobId, cancellationToken);
}
