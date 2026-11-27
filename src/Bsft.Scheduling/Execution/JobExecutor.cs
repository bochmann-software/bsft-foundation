using Bsft.Core.Tenancy;
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
    private readonly ITenantScopeFactory? _tenantScopes;
    private readonly TimeProvider _timeProvider;
    private readonly BsftSchedulingOptions _options;
    private readonly IJobTelemetry _telemetry;
    private readonly IJobRunResultRecorderFactory _resultRecorders;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutor"/> class.
    /// </summary>
    /// <param name="scopeFactory">Creates the scope the job and its dependencies are resolved from.</param>
    /// <param name="timeProvider">Supplies the start instant recorded in the execution context.</param>
    /// <param name="options">Scheduling options, read for the lease duration that bounds an execution.</param>
    /// <param name="telemetry">Telemetry sink for the execution span and duration.</param>
    /// <param name="resultRecorders">Creates the recorder each execution hands its job.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobExecutor(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<BsftSchedulingOptions> options,
        IJobTelemetry telemetry,
        IJobRunResultRecorderFactory resultRecorders)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _resultRecorders = resultRecorders ?? throw new ArgumentNullException(nameof(resultRecorders));
        _options = options.Value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutor"/> class that opens a job's scope through
    /// <paramref name="tenantScopes"/> while a background pass works for a tenant
    /// (<see cref="BackgroundTenant.Current"/>), so that the host can prepare the tenant's scope before the job is
    /// resolved. The container picks it when an <see cref="ITenantScopeFactory"/> is registered.
    /// </summary>
    /// <param name="scopeFactory">Creates the job's scope outside a tenant's pass.</param>
    /// <param name="timeProvider">The clock the lease is measured on.</param>
    /// <param name="options">The scheduling options carrying the lease duration.</param>
    /// <param name="telemetry">Records each execution.</param>
    /// <param name="resultRecorders">Creates the recorder each execution hands its job.</param>
    /// <param name="tenantScopes">Creates the job's scope for the tenant of the pass.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobExecutor(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<BsftSchedulingOptions> options,
        IJobTelemetry telemetry,
        IJobRunResultRecorderFactory resultRecorders,
        ITenantScopeFactory tenantScopes)
        : this(scopeFactory, timeProvider, options, telemetry, resultRecorders)
    {
        _tenantScopes = tenantScopes ?? throw new ArgumentNullException(nameof(tenantScopes));
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
            run.Payload)
        {
            ResultRecorder = _resultRecorders.Create(run),
        };

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

            await using var scope = await CreateJobScopeAsync(linked.Token).ConfigureAwait(false);
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

    /// <summary>
    /// Opens the scope a job runs in: the tenant's, through the host's factory, while a background pass works
    /// for a tenant, and a plain one otherwise.
    /// </summary>
    /// <param name="cancellationToken">The run's token, which bounds binding the tenant's scope as it bounds the job.</param>
    /// <remarks>
    /// The tenant's scope is opened asynchronously, so a host that binds it with I/O does not block the
    /// dispatcher's thread, and either scope is disposed asynchronously, so a service in it that is only
    /// <see cref="IAsyncDisposable"/> is released without a fault.
    /// </remarks>
    private ValueTask<TenantPassScope> CreateJobScopeAsync(CancellationToken cancellationToken) =>
        TenantPassScope.CreateAsync(_scopeFactory, _tenantScopes, cancellationToken);
}
