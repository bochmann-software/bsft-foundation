using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.StoredSchedules;

/// <summary>
/// Background service that turns due rows of the persisted, per-tenant schedule catalog into ordinary
/// one-shot job runs, the same shape <see cref="IJobScheduler.EnqueueAtAsync"/> produces.
/// </summary>
/// <remarks>
/// <para>
/// A stored schedule is never a repeating timer in memory, in the same way a code-registered recurring
/// job is not one: <see cref="StoredSchedule.NextRunAt"/> is the single due slot, advanced atomically by
/// <see cref="IStoredScheduleStore.AdvanceOrNullAsync"/> so that exactly one instance turns it into a run
/// when several are polling the same catalog.
/// </para>
/// <para>
/// The run this seeder writes carries no recurring marker of its own — it is claimed, executed and
/// retried by the existing <see cref="Execution.JobDispatcher"/> and <see cref="Execution.JobRunner"/> exactly like any
/// other one-shot job, which is what lets the persisted catalog and the code-registered path share one
/// dispatcher without either knowing the other exists.
/// </para>
/// </remarks>
public sealed class StoredScheduleSeeder : BackgroundService
{
    private readonly IStoredScheduleStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly BsftSchedulingOptions _options;
    private readonly ILogger<StoredScheduleSeeder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredScheduleSeeder"/> class.
    /// </summary>
    /// <param name="store">The catalog polled for due schedules.</param>
    /// <param name="scopeFactory">Creates the scope the scoped <see cref="IJobScheduler"/> is resolved from.</param>
    /// <param name="timeProvider">Supplies the instant a schedule is measured due against.</param>
    /// <param name="options">Scheduling options, already validated by <c>ValidateOnStart()</c>.</param>
    /// <param name="logger">Records a schedule that has run out of occurrences and a failed pass.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public StoredScheduleSeeder(
        IStoredScheduleStore store,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<BsftSchedulingOptions> options,
        ILogger<StoredScheduleSeeder> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => SeedPeriodicallyAsync(stoppingToken);

    /// <summary>
    /// Seeds the due stored schedules and keeps doing so until the stopping token is cancelled: on the
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

            var interval = _options.PollInterval;

            if (attempt.IsCompletedSuccessfully)
            {
                interval = _options.StoredScheduleSeedInterval;
            }
            else
            {
                _logger.LogError(attempt.Exception, "Seeding the stored schedules failed; retrying in {RetryInterval}.", interval);
            }

            var backoff = Task.Delay(interval, _timeProvider, stoppingToken);
            await Task.WhenAny(backoff).ConfigureAwait(false);

            if (backoff.IsCanceled)
                break;
        }

        _logger.LogDebug("Stored schedule seeding stopped.");
    }

    /// <summary>
    /// One pass: reads the due schedules, advances each one past its slot and enqueues the run for
    /// whichever advance this instance won.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    private async Task AttemptSeedAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var due = await _store.ListDueAsync(now, _options.StoredScheduleBatchSize, cancellationToken).ConfigureAwait(false);
        if (due.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var jobScheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();

        foreach (var schedule in due)
        {
            await SeedOneAsync(schedule, now, jobScheduler, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Advances a single due schedule and, when this instance won the race, enqueues its run.
    /// </summary>
    private async Task SeedOneAsync(StoredSchedule schedule, DateTimeOffset now, IJobScheduler jobScheduler, CancellationToken cancellationToken)
    {
        var dueAt = schedule.NextRunAt!.Value;

        // Anchoring on the later of the two keeps a schedule that fell behind from replaying every
        // missed tick in a tight loop, matching RecurringJobPlanner's reasoning for code-registered jobs.
        var anchor = dueAt > now ? dueAt : now;
        var next = schedule.Schedule.FindNextOccurrence(anchor);

        var advanced = await _store.AdvanceOrNullAsync(schedule, now, next, cancellationToken).ConfigureAwait(false);

        // Losing the race is the normal outcome when several instances poll the same catalog, not an
        // error: the winner enqueues the run.
        if (advanced is null)
            return;

        if (next is null)
        {
            _logger.LogWarning(
                "The schedule '{Schedule}' of stored schedule {ScheduleId} (job {JobKey}) has no further occurrence; it will not run again until it is edited.",
                schedule.Schedule,
                schedule.Id,
                schedule.JobKey);
        }

        await jobScheduler.EnqueueAtAsync(advanced.JobKey, dueAt, advanced.Payload, cancellationToken).ConfigureAwait(false);
    }
}
