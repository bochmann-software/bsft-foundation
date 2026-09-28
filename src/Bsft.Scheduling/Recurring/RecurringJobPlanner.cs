using Microsoft.Extensions.Logging;

namespace Bsft.Scheduling.Recurring;

/// <summary>
/// Default <see cref="IRecurringJobPlanner"/>.
/// </summary>
internal sealed class RecurringJobPlanner : IRecurringJobPlanner
{
    private readonly IJobStore _store;
    private readonly IJobRegistry _registry;
    private readonly IJobScheduleResolver _schedules;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecurringJobPlanner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringJobPlanner"/> class.
    /// </summary>
    /// <param name="store">The store occurrences are written to.</param>
    /// <param name="registry">The registry of known jobs.</param>
    /// <param name="schedules">Supplies the recurrence rule of a job, wherever it was stated.</param>
    /// <param name="timeProvider">Supplies the current instant the next occurrence is anchored to.</param>
    /// <param name="logger">Records a schedule that has run out of occurrences.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RecurringJobPlanner(
        IJobStore store,
        IJobRegistry registry,
        IJobScheduleResolver schedules,
        TimeProvider timeProvider,
        ILogger<RecurringJobPlanner> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _schedules = schedules ?? throw new ArgumentNullException(nameof(schedules));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> TryScheduleNextAsync(string jobName, DateTimeOffset previousDueAt, CancellationToken cancellationToken = default)
    {
        if (jobName is null)
            throw new ArgumentNullException(nameof(jobName));

        var definition = _registry.Find(jobName);
        if (definition is null || !definition.IsRecurring)
            return false;

        // A job switched off gets no occurrence written, which is the whole of what disabling it means
        // at this end: nothing to claim, nothing to execute. It is asked here rather than at
        // registration so that switching it back on needs no restart — the seeder comes past on its
        // own interval and the chain resumes.
        if (!_registry.IsEnabled(jobName))
            return false;

        // Anchoring on the later of the two keeps a job that fell behind from replaying every missed
        // tick in a tight loop, and keeps a run that outlasted its own interval from scheduling an
        // occurrence that is already overdue the moment it is written.
        var anchor = Max(previousDueAt, _timeProvider.GetUtcNow());
        var schedule = _schedules.Resolve(jobName);
        var next = schedule.FindNextOccurrence(anchor);

        if (next is null)
        {
            // A cron expression can be valid and still have no future match — "0 0 30 2 *". Saying so
            // once beats a job that simply never runs again with nothing in the log.
            _logger.LogWarning(
                "The schedule '{Schedule}' of job {JobName} has no further occurrence; the job will not run again.",
                schedule,
                definition.Name);
            return false;
        }

        var run = new JobRun
        {
            JobName = definition.Name,
            DueAt = next.Value,
            Status = JobRunStatus.Pending,
            IsRecurring = true,
        };

        return await _store.TryEnqueueRecurringAsync(run, definition.OverlapPolicy, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var definition in _registry.Definitions.Where(d => d.IsRecurring))
        {
            await TryScheduleNextAsync(definition.Name, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
}
