using System.Globalization;
using Bsft.Scheduling.Recurring;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.Heartbeats;

/// <summary>
/// Reports recurring jobs that have not completed a run for longer than their own cadence allows.
/// </summary>
/// <remarks>
/// <para>
/// The tolerance of each job comes from that job's <see cref="BsftJobOptions"/> and, where those leave
/// it unset, from the job's own schedule. Nothing about it is stated at the call site, so a host does
/// not carry a table of job names and grace periods, and renaming or rescheduling a job cannot leave a
/// stale tolerance behind in a second place.
/// </para>
/// <para>
/// It reports <see cref="HealthStatus.Degraded"/> and never <see cref="HealthStatus.Unhealthy"/>, for
/// the reason a scheduling signal is not a readiness signal: an instance whose jobs are late still
/// serves requests correctly, and failing readiness on it would take a healthy instance out of
/// rotation and hand its jobs to the next one, which is how one late job becomes a rolling outage. An
/// operator who does want it gating readiness adds the readiness tag to this registration
/// deliberately.
/// </para>
/// <para>
/// The answer is per instance, because <see cref="IJobHeartbeatStore"/> is: a job runs once across a
/// deployment, so what this reports is what this instance has seen since it started.
/// </para>
/// </remarks>
internal sealed class JobHeartbeatHealthCheck : IHealthCheck
{
    /// <summary>The name this check is registered under.</summary>
    internal const string Name = "bsft-scheduling-jobs";

    /// <summary>The tag this check carries, so it can be selected without selecting readiness.</summary>
    internal const string Tag = "bsft.scheduling";

    private readonly IJobRegistry _registry;
    private readonly IJobScheduleResolver _schedules;
    private readonly IJobHeartbeatStore _heartbeats;
    private readonly IOptionsMonitor<BsftJobOptions> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobHeartbeatHealthCheck"/> class.
    /// </summary>
    /// <param name="registry">The registry the watched jobs are read from.</param>
    /// <param name="schedules">Supplies the cadence a job's tolerance is derived from.</param>
    /// <param name="heartbeats">Supplies when each job last completed a run.</param>
    /// <param name="options">Supplies each job's own tolerance and switches.</param>
    /// <param name="timeProvider">Supplies the instant the silence is measured against.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobHeartbeatHealthCheck(
        IJobRegistry registry,
        IJobScheduleResolver schedules,
        IJobHeartbeatStore heartbeats,
        IOptionsMonitor<BsftJobOptions> options,
        TimeProvider timeProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _schedules = schedules ?? throw new ArgumentNullException(nameof(schedules));
        _heartbeats = heartbeats ?? throw new ArgumentNullException(nameof(heartbeats));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var watched = new Dictionary<string, object>(StringComparer.Ordinal);
        var overdue = new List<string>();

        foreach (var definition in _registry.Definitions)
        {
            var snapshot = await MeasureAsync(definition, now, cancellationToken).ConfigureAwait(false);

            if (snapshot is null)
                continue;

            watched[snapshot.JobName] = snapshot;

            if (snapshot.IsOverdue)
                overdue.Add(snapshot.JobName);
        }

        if (overdue.Count > 0)
        {
            return HealthCheckResult.Degraded(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} of {1} watched recurring job(s) have not completed a run within their heartbeat tolerance: {2}.",
                    overdue.Count,
                    watched.Count,
                    string.Join(", ", overdue)),
                data: watched);
        }

        return HealthCheckResult.Healthy(
            watched.Count == 0
                ? "No recurring job is being watched for liveness."
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} watched recurring job(s) are within their heartbeat tolerance.",
                    watched.Count),
            data: watched);
    }

    /// <summary>Measures one job, unless it is not watched or there is nothing to measure it against.</summary>
    /// <param name="definition">The job registration.</param>
    /// <param name="now">The instant the silence is measured against.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// The snapshot, or <c>null</c> when the job is not watched, or has no tolerance and none can be
    /// derived.
    /// </returns>
    private async Task<JobHeartbeatSnapshot?> MeasureAsync(
        JobDefinition definition, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A job that runs when something enqueues it has no cadence to be late against; a job switched
        // off is not late, it is off; and one whose watch was turned off is a job whose silence its
        // owner has already said means nothing.
        if (!definition.IsRecurring)
            return null;

        var options = _options.Get(definition.Name);

        if (!options.Enabled || !options.IsHeartbeatMonitored)
            return null;

        var expected = options.HeartbeatTolerance is null ? ExpectedInterval(definition, now) : null;

        var tolerance = options.HeartbeatTolerance
            ?? (expected is { } gap ? Widen(gap, options.HeartbeatToleranceFactor, options.HeartbeatGrace) : null);

        // A cron expression that has no two further occurrences — one that has run out, or one that
        // fires once ever — gives nothing to derive a cadence from. Guessing one would report a job as
        // late for a schedule it is keeping, so the job is left unwatched until somebody states a
        // tolerance for it.
        if (tolerance is not { } allowed)
            return null;

        var lastCompletedAt = await _heartbeats.LastCompletedAtAsync(definition.Name, cancellationToken).ConfigureAwait(false);
        var measuredFrom = lastCompletedAt ?? _heartbeats.ObservingSince;

        return new JobHeartbeatSnapshot(
            definition.Name, lastCompletedAt, measuredFrom, now - measuredFrom, allowed, expected);
    }

    /// <summary>Widens an interval by the configured factor and adds the configured grace.</summary>
    /// <param name="interval">The gap between two occurrences of the job's schedule.</param>
    /// <param name="factor">How many of those intervals the job may miss. Validated as finite and at least one.</param>
    /// <param name="grace">What is added on top.</param>
    /// <returns>The tolerance, saturated rather than overflowed.</returns>
    /// <remarks>
    /// The arithmetic is done on ticks as a <see cref="double"/> and saturated, because
    /// <c>TimeSpan * double</c> throws on overflow: a job configured with a very long interval and a
    /// large factor would otherwise take the health endpoint down with an
    /// <see cref="OverflowException"/> rather than report the job as never late, which is what such a
    /// tolerance means.
    /// </remarks>
    private static TimeSpan Widen(TimeSpan interval, double factor, TimeSpan grace)
    {
        var ticks = (interval.Ticks * factor) + grace.Ticks;

        return ticks >= long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>Works out the gap between two occurrences of a job's schedule.</summary>
    /// <param name="definition">The job registration.</param>
    /// <param name="now">The instant the schedule is read forward from.</param>
    /// <returns>The gap, or <c>null</c> when the schedule has no two further occurrences.</returns>
    /// <remarks>
    /// An interval schedule states the gap outright. A cron expression does not — its gap varies with
    /// where in the calendar it is asked — so the next two occurrences from now are what it is measured
    /// as, which is the same window the job is about to be judged over.
    /// </remarks>
    private TimeSpan? ExpectedInterval(JobDefinition definition, DateTimeOffset now)
    {
        var schedule = _schedules.Resolve(definition.Name);

        if (schedule.Interval is { } interval)
            return interval;

        return schedule.FindNextOccurrence(now) is { } next && schedule.FindNextOccurrence(next) is { } following
            ? following - next
            : null;
    }
}
