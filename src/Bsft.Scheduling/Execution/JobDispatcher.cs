using Bsft.Core.Polling;
using Bsft.Scheduling.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.Execution;

/// <summary>
/// Background service that drives the schedule: it polls the <see cref="IJobStore"/> for runs that
/// are due, takes a lease on each one and hands it to the <see cref="IJobRunner"/>.
/// </summary>
/// <remarks>
/// <para>
/// Several application instances may run this service against one store. The lease taken in
/// <see cref="IJobStore.ClaimAsync"/> decides which instance executes a given occurrence, so a
/// recurring job scheduled for three in the morning runs once across the deployment, not once per
/// instance.
/// </para>
/// <para>
/// Execution is at-least-once. An instance that dies while holding a lease lets it expire, and the
/// run is picked up again — possibly after the work was already done. Jobs must be idempotent.
/// </para>
/// </remarks>
public sealed class JobDispatcher : BackgroundService, IPollReporter
{
    private readonly IJobStore _store;
    private readonly IJobRunner _runner;
    private readonly TimeProvider _timeProvider;
    private readonly BsftSchedulingOptions _options;
    private readonly IJobTelemetry _telemetry;
    private readonly ILogger<JobDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobDispatcher"/> class.
    /// </summary>
    /// <param name="store">The store polled for claimable runs.</param>
    /// <param name="runner">Executes a claimed run and settles it.</param>
    /// <param name="timeProvider">Supplies the instant a run is measured due against.</param>
    /// <param name="options">Dispatcher options, already validated by <c>ValidateOnStart()</c>.</param>
    /// <param name="telemetry">Telemetry sink for the claim counter.</param>
    /// <param name="logger">Records a failed poll.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobDispatcher(
        IJobStore store,
        IJobRunner runner,
        TimeProvider timeProvider,
        IOptions<BsftSchedulingOptions> options,
        IJobTelemetry telemetry,
        ILogger<JobDispatcher> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _store = store ?? throw new ArgumentNullException(nameof(store));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the polling loop until the stopping token is cancelled.</summary>
    /// <remarks>
    /// The loop itself is <see cref="PollLoop"/>: a store that is briefly unreachable must cost one
    /// pass rather than the lifetime of the process, and neither may a pass that ended cancelled by a
    /// token of its own — a command timeout, typically — be read as a shutdown, which would leave the
    /// host running with nothing dispatching jobs any more.
    /// </remarks>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal Task RunLoopAsync(CancellationToken stoppingToken = default) =>
        new PollLoop(DispatchAsync, this, _options.PollInterval).RunAsync(stoppingToken);

    /// <summary>
    /// One pass of the loop. A pass that claimed something probably has more waiting, so it polls
    /// again at once rather than sleeping through a backlog.
    /// </summary>
    private async Task<PollPassOutcome> DispatchAsync(CancellationToken cancellationToken) =>
        await ProcessOnceAsync(cancellationToken).ConfigureAwait(false) > 0
            ? PollPassOutcome.PollAgain
            : PollPassOutcome.WaitForInterval;

    /// <inheritdoc />
    void IPollReporter.ReportPassFailed(Exception failure) =>
        _logger.LogError(failure, "The job dispatch pass failed.");

    /// <inheritdoc />
    void IPollReporter.ReportPassCanceledWithoutStop() =>
        _logger.LogError("The job dispatch pass was canceled although the dispatcher was not asked to stop.");

    /// <inheritdoc />
    void IPollReporter.ReportStopping() => _logger.LogDebug("The job dispatcher is stopping.");

    /// <summary>Claims and runs one batch of due runs, and reports how many were claimed.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of runs this instance claimed in this pass.</returns>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var claimable = await _store.ListClaimableAsync(now, _options.BatchSize, cancellationToken).ConfigureAwait(false);
        if (claimable.Count == 0)
            return 0;

        var handled = 0;

        foreach (var candidate in claimable)
        {
            // The claim instant is read per candidate, not once for the pass. The batch is executed
            // sequentially, so the instant the pass started is already stale by however long the
            // earlier runs took: deriving the lease from it would hand the later ones a lease that is
            // partly spent before their work begins, and with slow jobs one that lapses mid-execution
            // — at which point another instance reclaims the run and executes it in parallel.
            var claimAt = _timeProvider.GetUtcNow();

            var claimed = await _store
                .ClaimAsync(candidate, _options.InstanceId, claimAt, claimAt + _options.LeaseDuration, cancellationToken)
                .ConfigureAwait(false);

            // Losing the race is the normal outcome when several instances poll the same store, not
            // an error: the winner is running it.
            if (claimed is null)
                continue;

            handled++;
            _telemetry.JobClaimed(claimed.JobName);

            await _runner.RunAsync(claimed, cancellationToken).ConfigureAwait(false);
        }

        return handled;
    }
}
