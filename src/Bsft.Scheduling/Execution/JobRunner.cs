using Bsft.Scheduling.Recurring;

namespace Bsft.Scheduling.Execution;

/// <summary>
/// Default <see cref="IJobRunner"/>.
/// </summary>
internal sealed class JobRunner : IJobRunner
{
    private readonly IJobRegistry _registry;
    private readonly IJobExecutor _executor;
    private readonly IRecurringJobPlanner _planner;
    private readonly IJobRunOutcomePolicy _outcomes;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunner"/> class.
    /// </summary>
    /// <param name="registry">The registry the run's job name is resolved against.</param>
    /// <param name="executor">Executes the job itself.</param>
    /// <param name="planner">Creates the next occurrence of a recurring job.</param>
    /// <param name="outcomes">Decides and records what becomes of the run.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobRunner(
        IJobRegistry registry,
        IJobExecutor executor,
        IRecurringJobPlanner planner,
        IJobRunOutcomePolicy outcomes)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
    }

    /// <inheritdoc />
    public async Task RunAsync(JobRun claimed, CancellationToken cancellationToken = default)
    {
        if (claimed is null)
            throw new ArgumentNullException(nameof(claimed));

        var definition = _registry.Find(claimed.JobName);
        if (definition is null)
        {
            // The job was renamed or removed while its runs were still in the store.
            await _outcomes.RecordUnregisteredAsync(claimed, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Asked per run rather than once at startup, so that switching a job off takes effect on the
        // occurrences already in the store instead of only on the ones nobody has written yet. No
        // successor is planned: a disabled job stops, and the seeder restores its chain when it is
        // switched back on.
        if (!_registry.IsEnabled(claimed.JobName))
        {
            await _outcomes.RecordDisabledAsync(claimed, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_outcomes.IsBeyondCatchUpWindow(claimed))
        {
            await _outcomes.RecordSkippedAsync(claimed, cancellationToken).ConfigureAwait(false);

            // A skipped occurrence still has to produce the next one, or a recurring job would stop
            // for good the first time an outage outlasted the catch-up window.
            await _planner.TryScheduleNextAsync(claimed.JobName, claimed.DueAt, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Under Allow the following occurrence goes out before the work starts, so the job keeps its
        // cadence while a slow run is in flight. Under Forbid it waits until this one has settled,
        // which is what makes overlap impossible.
        if (definition.OverlapPolicy == JobOverlapPolicy.Allow)
            await _planner.TryScheduleNextAsync(claimed.JobName, claimed.DueAt, cancellationToken).ConfigureAwait(false);

        await ExecuteAndSettleAsync(definition, claimed, cancellationToken).ConfigureAwait(false);

        if (definition.OverlapPolicy == JobOverlapPolicy.Forbid)
            await _planner.TryScheduleNextAsync(claimed.JobName, claimed.DueAt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the job and records the outcome.
    /// </summary>
    /// <remarks>
    /// A job is foreign code that may fail in any way at all, so its outcome is inspected through
    /// the task rather than caught. <c>Task.WhenAny</c> completes without rethrowing, which records
    /// the failure without a catch-all clause that would also swallow the fatal conditions the
    /// scheduler was never meant to handle.
    /// </remarks>
    private async Task ExecuteAndSettleAsync(JobDefinition definition, JobRun claimed, CancellationToken cancellationToken)
    {
        var execution = InvokeExecutorAsync(definition.JobType, claimed, cancellationToken);
        await Task.WhenAny(execution).ConfigureAwait(false);

        if (execution.IsCompletedSuccessfully)
        {
            await _outcomes.RecordCompletedAsync(claimed, cancellationToken).ConfigureAwait(false);

            return;
        }

        // A cancellation raised while the dispatcher itself is stopping is a shutdown, not a verdict on
        // the job: the run is left claimed and whichever instance is still up recovers it once the
        // lease expires. Its attempt count is not preserved by doing so — ClaimAsync charges an attempt
        // on every reclaim — but there is nothing better to record while the process is going away.
        if (execution.IsCanceled && cancellationToken.IsCancellationRequested)
            return;

        await _outcomes.RecordFailureAsync(claimed, FailureOf(execution, claimed), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the failure a finished execution is recorded with.
    /// </summary>
    /// <remarks>
    /// An execution that ended cancelled while nobody asked the dispatcher to stop was cancelled by a
    /// token of its own — the lease boundary in the executor is the usual one, and a job whose work
    /// legitimately outlasts <see cref="BsftSchedulingOptions.LeaseDuration"/> hits it on every attempt.
    /// That has to be charged as a failed attempt. Settling nothing would leave the row running under a
    /// lapsed lease, the poll would reclaim it, and since <see cref="BsftSchedulingOptions.MaxAttempts"/> is
    /// only consulted when a failure is recorded, the job would be executed again for as long as the
    /// application lives, never reaching a terminal state and never saying so in the log.
    /// </remarks>
    private static Exception FailureOf(Task execution, JobRun run)
    {
        if (execution.Exception is { } faulted)
            return faulted;

        return new TaskCanceledException(
            $"Job {run.JobName} run {run.Id} was canceled by a token of its own rather than by a shutdown of the dispatcher; the work did not finish inside its lease.");
    }

    /// <summary>
    /// Invokes the executor from inside an async method, so that an implementation which throws
    /// synchronously — before ever returning a task — still surfaces as a faulted task the caller can
    /// inspect, rather than escaping past it and stopping the dispatcher.
    /// </summary>
    private async Task InvokeExecutorAsync(Type jobType, JobRun run, CancellationToken cancellationToken) =>
        await _executor.ExecuteAsync(jobType, run, cancellationToken).ConfigureAwait(false);
}
