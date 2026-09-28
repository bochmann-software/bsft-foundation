using Bsft.Core.Polling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Retention;

/// <summary>
/// Background service that runs one <see cref="IRetentionPass"/> over and over: it computes the
/// cutoff, drains the backlog a batch at a time, paces the batches and offsets its first sweep.
/// </summary>
/// <typeparam name="TPass">The retention pass this sweeper drives.</typeparam>
/// <remarks>
/// One sweeper per pass, so the passes of a host neither share a schedule nor block one another. The
/// generic parameter is what keeps them apart in the container, and it is also how the sweeper finds
/// its own named <see cref="BsftRetentionOptions"/>.
/// </remarks>
public sealed class RetentionSweeper<TPass> : BackgroundService, IPollReporter
    where TPass : class, IRetentionPass
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BsftRetentionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RetentionSweeper<TPass>> _logger;
    private readonly string _passName;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetentionSweeper{TPass}"/> class.
    /// </summary>
    /// <param name="scopeFactory">Creates a scope per sweep, so a pass may use scoped services.</param>
    /// <param name="options">Supplies this pass's named options.</param>
    /// <param name="timeProvider">Works out the cutoff and drives the stagger and the pacing.</param>
    /// <param name="logger">Records what the sweeps deleted.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RetentionSweeper(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<BsftRetentionOptions> options,
        TimeProvider timeProvider,
        ILogger<RetentionSweeper<TPass>> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passName = BsftRetentionOptions.NameFor<TPass>();
        _options = options.Get(_passName);
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the sweep loop until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal async Task RunLoopAsync(CancellationToken stoppingToken = default)
    {
        if (_options.RetentionPeriod is not { } retention)
        {
            _logger.LogDebug(
                "The retention pass {RetentionPass} is not swept because no retention period is configured; every record is kept indefinitely.",
                _passName);

            return;
        }

        var stagger = RetentionStagger.OffsetFor(_passName, _options.MaxStartupStagger);

        if (stagger > TimeSpan.Zero)
        {
            _logger.LogDebug(
                "The retention pass {RetentionPass} starts sweeping in {StaggerDelay}.", _passName, stagger);

            try
            {
                await Task.Delay(stagger, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The host stopped inside the offset, before the loop ever started. There is nothing
                // to report and nothing to unwind: the sweeper simply never ran.
                return;
            }
        }

        // Sweeping at the retention itself keeps a short retention honest without a second option to
        // configure, and the cap keeps a long one from becoming a rare, enormous delete.
        var interval = retention < _options.SweepInterval ? retention : _options.SweepInterval;

        await new PollLoop(ct => SweepAsync(retention, ct), this, interval)
            .RunAsync(stoppingToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    void IPollReporter.ReportPassFailed(Exception failure) =>
        _logger.LogError(
            failure,
            "The retention sweep of {RetentionPass} failed; it runs again after the interval.",
            _passName);

    /// <inheritdoc />
    void IPollReporter.ReportPassCanceledWithoutStop() =>
        _logger.LogError(
            "The retention sweep of {RetentionPass} was canceled although the sweeper was not asked to stop.",
            _passName);

    /// <inheritdoc />
    void IPollReporter.ReportStopping() =>
        _logger.LogDebug("The retention sweeper of {RetentionPass} is stopping.", _passName);

    /// <summary>
    /// One sweep: batches against a single cutoff until the backlog is drained, the batch bound is
    /// reached or the host stops.
    /// </summary>
    /// <param name="retention">How long a record is kept.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>Whether the loop should come straight back round.</returns>
    /// <remarks>
    /// The cutoff is computed once per sweep rather than once per batch, so a long drain deletes the
    /// set it set out to delete instead of a target that creeps forward under it — which is what makes
    /// the sweep terminate on a table receiving writes faster than one batch clears.
    /// </remarks>
    internal async Task<PollPassOutcome> SweepAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow() - retention;

        using var scope = _scopeFactory.CreateScope();
        var pass = scope.ServiceProvider.GetRequiredService<TPass>();

        var total = 0;

        for (var batch = 0; batch < _options.MaxBatchesPerSweep; batch++)
        {
            var purged = await pass.PurgeBatchAsync(_options.BatchSize, cutoff, cancellationToken).ConfigureAwait(false);
            total += purged;

            // A short batch is the backlog reporting itself drained; only a full one means more is
            // waiting behind it.
            if (purged < _options.BatchSize)
            {
                LogSwept(total, cutoff);

                return PollPassOutcome.WaitForInterval;
            }

            if (_options.BatchDelay > TimeSpan.Zero)
                await Task.Delay(_options.BatchDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        LogSwept(total, cutoff);

        // The bound was reached with a full batch still coming back, so the backlog outlasted this
        // sweep. Polling again at once drains it instead of trickling it out one interval at a time.
        return PollPassOutcome.PollAgain;
    }

    /// <summary>Reports a sweep that deleted something.</summary>
    /// <param name="total">How many records the sweep deleted in all.</param>
    /// <param name="cutoff">The cutoff the sweep deleted against.</param>
    private void LogSwept(int total, DateTimeOffset cutoff)
    {
        if (total > 0)
        {
            _logger.LogInformation(
                "The retention pass {RetentionPass} purged {PurgedCount} records older than {Cutoff:o}.",
                _passName,
                total,
                cutoff);
        }
    }
}
