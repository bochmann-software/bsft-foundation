using Bsft.Scheduling.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.Execution;

/// <summary>
/// Default <see cref="IJobExecutor"/>.
/// </summary>
internal sealed class JobExecutor : IJobExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly BsftSchedulingOptions _options;
    private readonly IJobTelemetry _telemetry;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutor"/> class.
    /// </summary>
    /// <param name="scopeFactory">Creates the scope the job and its dependencies are resolved from.</param>
    /// <param name="timeProvider">Supplies the start instant recorded in the execution context.</param>
    /// <param name="options">Scheduling options, read for the lease duration that bounds an execution.</param>
    /// <param name="telemetry">Telemetry sink for the execution span and duration.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobExecutor(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<BsftSchedulingOptions> options,
        IJobTelemetry telemetry)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(Type jobType, JobRun run, CancellationToken cancellationToken = default)
    {
        if (jobType is null)
            throw new ArgumentNullException(nameof(jobType));
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        var startedAt = _timeProvider.GetUtcNow();

        var context = new JobExecutionContext(
            run.Id,
            run.JobName,
            run.DueAt,
            startedAt,
            run.AttemptCount,
            run.CorrelationId,
            run.Payload);

        // The correlation id is restored around the scope, not inside it, so that everything resolved
        // from the scope — loggers, the correlation context, an audit sink — sees the id of the
        // request that enqueued the run rather than minting a fresh one.
        using (JobCorrelationScope.Use(run.CorrelationId))
        {
            // A job outliving its lease would be picked up and run a second time in parallel, which
            // is precisely what the lease exists to prevent. Cancelling at the lease boundary turns
            // that into a failed attempt the retry logic can see.
            using var timeout = new CancellationTokenSource(RemainingLease(run, startedAt), _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            using var scope = _scopeFactory.CreateScope();
            var job = (IJob)scope.ServiceProvider.GetRequiredService(jobType);

            using var telemetryScope = _telemetry.BeginExecution(run.JobName, run.Id, run.AttemptCount);
            await job.ExecuteAsync(context, linked.Token).ConfigureAwait(false);
            telemetryScope.Succeeded();
        }
    }

    /// <summary>
    /// Reads how much of the run's lease is left to execute in.
    /// </summary>
    /// <remarks>
    /// The lease starts running at claim time, and work happens between the claim and this method:
    /// the dispatcher walks its batch sequentially, and under
    /// <see cref="JobOverlapPolicy.Allow"/> the next occurrence is written to the store before the
    /// job starts. Granting the execution a fresh <see cref="BsftSchedulingOptions.LeaseDuration"/> here
    /// would therefore always end it strictly after the lease it is meant to stay inside, and a job
    /// running into that window is still executing while <c>ListClaimableAsync</c> already offers the
    /// run to every other instance — the parallel second execution the boundary exists to prevent.
    /// A lease that has already lapsed yields zero, which cancels before the job is invoked: the run
    /// belongs to whoever reclaims it, not to this instance.
    /// </remarks>
    /// <param name="run">The claimed run whose lease bounds the execution.</param>
    /// <param name="startedAt">The instant the execution starts.</param>
    /// <returns>The time left until the lease expires, or the configured duration when the run carries no lease.</returns>
    private TimeSpan RemainingLease(JobRun run, DateTimeOffset startedAt)
    {
        // A run without a lease was never claimed through the dispatcher — an executor invoked
        // directly, or a store that does not lease. The configured duration is the only bound there is.
        if (run.LeaseExpiresAt is not { } expiresAt)
            return _options.LeaseDuration;

        var remaining = expiresAt - startedAt;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
