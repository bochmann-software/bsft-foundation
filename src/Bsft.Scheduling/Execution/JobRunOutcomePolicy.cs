using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Heartbeats;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.Execution;

/// <summary>
/// Default <see cref="IJobRunOutcomePolicy"/>.
/// </summary>
internal sealed class JobRunOutcomePolicy : IJobRunOutcomePolicy
{
    private readonly IJobStore _store;
    private readonly BsftSchedulingOptions _options;
    private readonly IJobTelemetry _telemetry;
    private readonly IJobHeartbeatStore _heartbeats;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobRunOutcomePolicy> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunOutcomePolicy"/> class.
    /// </summary>
    /// <param name="store">The store the outcome is written to.</param>
    /// <param name="options">Scheduling options governing retries and catch-up.</param>
    /// <param name="telemetry">Telemetry sink for the skipped counter.</param>
    /// <param name="heartbeats">Told about every run that completed, which is what liveness is read from.</param>
    /// <param name="timeProvider">Supplies the instants recorded on the run.</param>
    /// <param name="logger">Records failures and orphaned runs.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobRunOutcomePolicy(
        IJobStore store,
        IOptions<BsftSchedulingOptions> options,
        IJobTelemetry telemetry,
        IJobHeartbeatStore heartbeats,
        TimeProvider timeProvider,
        ILogger<JobRunOutcomePolicy> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _store = store ?? throw new ArgumentNullException(nameof(store));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _heartbeats = heartbeats ?? throw new ArgumentNullException(nameof(heartbeats));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
    }

    /// <inheritdoc />
    public bool IsBeyondCatchUpWindow(JobRun run)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        return _options.CatchUpPolicy == JobCatchUpPolicy.SkipBeyondMaxAge
            && _timeProvider.GetUtcNow() - run.DueAt > _options.MaxCatchUpAge;
    }

    /// <inheritdoc />
    public async Task RecordCompletedAsync(JobRun run, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        await _store.SettleAsync(run, JobRunStatus.Completed, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        // Recorded where the completion is recorded, so that liveness cannot drift from what the store
        // says: a run that settled as completed is exactly the event the health check measures against.
        await _heartbeats.RecordCompletedAsync(run.JobName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordSkippedAsync(JobRun run, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        _logger.LogInformation(
            "Job run {JobId} of {JobName} was due at {DueAt} and is beyond the catch-up window; skipping it.",
            run.Id,
            run.JobName,
            run.DueAt);

        await _store.SettleAsync(run, JobRunStatus.Skipped, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        _telemetry.JobSkipped(run.JobName);
    }

    /// <inheritdoc />
    public async Task RecordDisabledAsync(JobRun run, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        _logger.LogInformation(
            "Job run {JobId} of {JobName} was due at {DueAt} but the job is disabled; skipping it.",
            run.Id,
            run.JobName,
            run.DueAt);

        await _store.SettleAsync(run, JobRunStatus.Skipped, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        _telemetry.JobSkipped(run.JobName);
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(JobRun run, Exception failure, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        var now = _timeProvider.GetUtcNow();

        // AttemptCount was incremented by the claim, so it already counts the attempt that just failed.
        if (run.AttemptCount < _options.MaxAttempts)
        {
            _logger.LogWarning(
                failure,
                "Job {JobName} run {JobId} failed on attempt {Attempt} of {MaxAttempts}; retrying.",
                run.JobName,
                run.Id,
                run.AttemptCount,
                _options.MaxAttempts);

            await _store.RetryAsync(run, now + _options.RetryDelay, cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogError(
            failure,
            "Job {JobName} run {JobId} failed on its final attempt {Attempt} and will not be retried.",
            run.JobName,
            run.Id,
            run.AttemptCount);

        await _store.SettleAsync(run, JobRunStatus.Failed, now, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordUnregisteredAsync(JobRun run, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        _logger.LogWarning(
            "Job run {JobId} names the unregistered job {JobName} and cannot be executed; marking it failed.",
            run.Id,
            run.JobName);

        await _store.SettleAsync(run, JobRunStatus.Failed, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }
}
