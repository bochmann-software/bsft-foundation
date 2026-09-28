using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.Recurring;

/// <summary>
/// Background service that keeps every recurring registration supplied with an occurrence.
/// </summary>
/// <remarks>
/// <para>
/// Seeding is idempotent and runs on every instance: a job that already has a pending or running
/// occurrence — from another instance, or from before the last restart — is left alone.
/// </para>
/// <para>
/// It retries a failed attempt rather than failing the host, because a store that is not reachable
/// yet is ordinary during a rolling deployment.
/// </para>
/// <para>
/// Seeding then repeats on <see cref="BsftSchedulingOptions.RecurringSeedInterval"/> rather than stopping
/// after the first success. The schedule does normally sustain itself, since each settled occurrence
/// creates the next one, but that chain has a gap: a crash or a store failure between settling a run
/// and writing its successor leaves the job with no pending occurrence. Seeding only at startup would
/// leave it silent until some instance happened to restart, with nothing to say so.
/// </para>
/// </remarks>
public sealed class RecurringJobSeeder : BackgroundService
{
    private readonly IRecurringJobPlanner _planner;
    private readonly TimeProvider _timeProvider;
    private readonly BsftSchedulingOptions _options;
    private readonly ILogger<RecurringJobSeeder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringJobSeeder"/> class.
    /// </summary>
    /// <param name="planner">Creates the occurrences.</param>
    /// <param name="timeProvider">Supplies the delay between seeding attempts.</param>
    /// <param name="options">Scheduling options, read for the retry interval.</param>
    /// <param name="logger">Records a seeding failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RecurringJobSeeder(
        IRecurringJobPlanner planner,
        TimeProvider timeProvider,
        IOptions<BsftSchedulingOptions> options,
        ILogger<RecurringJobSeeder> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => SeedPeriodicallyAsync(stoppingToken);

    /// <summary>
    /// Seeds the recurring jobs and keeps doing so until the stopping token is cancelled: on the
    /// configured seed interval after a success, and on the poll interval after a failure.
    /// </summary>
    /// <param name="stoppingToken">The token that stops the attempts when cancelled.</param>
    /// <returns>A task that completes once the host is stopping.</returns>
    internal async Task SeedPeriodicallyAsync(CancellationToken stoppingToken = default)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Inspected through the task rather than caught, so a failing store is retried without a
            // catch-all clause that would also swallow conditions nothing here can recover from.
            var attempt = AttemptSeedAsync(stoppingToken);
            await Task.WhenAny(attempt).ConfigureAwait(false);

            if (attempt.IsCanceled)
                break;

            // A failure comes back on the shorter poll interval, since a store that is not reachable
            // yet leaves the jobs with no occurrence at all; a success only has to catch a chain that
            // broke later, so it can wait out the seed interval.
            var interval = _options.PollInterval;

            if (attempt.IsCompletedSuccessfully)
            {
                interval = _options.RecurringSeedInterval;
            }
            else
            {
                _logger.LogError(attempt.Exception, "Seeding the recurring jobs failed; retrying in {RetryInterval}.", interval);
            }

            var backoff = Task.Delay(interval, _timeProvider, stoppingToken);
            await Task.WhenAny(backoff).ConfigureAwait(false);

            if (backoff.IsCanceled)
                break;
        }

        _logger.LogDebug("Recurring job seeding stopped.");
    }

    /// <summary>
    /// Invokes the planner from inside an async method, so that an implementation which throws
    /// synchronously — before ever returning a task — still surfaces as a faulted task the loop can
    /// inspect, rather than escaping past it and taking the host down.
    /// </summary>
    private async Task AttemptSeedAsync(CancellationToken cancellationToken) =>
        await _planner.SeedAsync(cancellationToken).ConfigureAwait(false);
}
