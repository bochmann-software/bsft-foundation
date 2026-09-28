namespace Bsft.Scheduling;

/// <summary>
/// What an operator decides about one registered job: whether it runs at all in this deployment, and
/// how long it may stay silent before the liveness check calls it overdue.
/// </summary>
/// <remarks>
/// <para>
/// These options are registered as <em>named</em> options, keyed by the registered job name, so every
/// job is configured independently and a host writes
/// <c>Bsft:Scheduling:Jobs:&lt;job name&gt;</c>. The <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// overload of <c>AddBsftScheduling</c> binds those sections for whatever jobs are registered, in any
/// order.
/// </para>
/// <para>
/// <see cref="Enabled"/> is read at runtime rather than at registration. A host that gates a job by
/// binding its section a second time — to decide whether to call <c>AddBsftRecurringJob</c> at all —
/// has a second source of truth for the same switch, one that only takes effect on a restart and that
/// no validator ever sees. Registering the job unconditionally and letting the scheduler read this
/// flag through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> makes the switch
/// one value, honoured on a configuration reload.
/// </para>
/// </remarks>
public sealed class BsftJobOptions
{
    /// <summary>The configuration section the per-job sections sit under.</summary>
    public const string SectionName = "Bsft:Scheduling:Jobs";

    /// <summary>
    /// Gets or sets a value indicating whether this job runs in this deployment. Defaults to
    /// <c>true</c>.
    /// </summary>
    /// <remarks>
    /// A disabled job is registered, resolvable and visible in the registry; it simply gets no new
    /// occurrences, and an occurrence still pending from before it was disabled is settled as skipped
    /// rather than executed. Re-enabling it needs no restart: the recurring seeder restores the chain
    /// on its next pass.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the liveness health check watches this job. Defaults to
    /// <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Only recurring jobs are watched at all — a job that runs when something enqueues it has no
    /// cadence to be late against. This turns the watch off for a recurring job whose silence is not
    /// an incident, such as one that only does anything on the first of the month.
    /// </remarks>
    public bool IsHeartbeatMonitored { get; set; } = true;

    /// <summary>
    /// Gets or sets how long this job may go without completing a run before the liveness check calls
    /// it overdue. Defaults to <c>null</c>, which derives the tolerance from the job's own schedule.
    /// </summary>
    /// <remarks>
    /// Set it where the derived tolerance is wrong for the job: one whose work legitimately takes
    /// longer than its own interval, or one whose lateness only matters at a much coarser grain than
    /// its cadence. Where it is set, <see cref="HeartbeatToleranceFactor"/> and
    /// <see cref="HeartbeatGrace"/> are not consulted.
    /// </remarks>
    public TimeSpan? HeartbeatTolerance { get; set; }

    /// <summary>
    /// Gets or sets how many of the job's own intervals it may miss before it is called overdue.
    /// Defaults to two.
    /// </summary>
    /// <remarks>
    /// A factor rather than an absolute window, because the only thing that makes "late" mean the same
    /// for a job running every minute and one running every night is its own cadence. Two is one
    /// missed occurrence: enough that a single slow run, a rolling restart or a claim lost to another
    /// instance does not raise an alert, and little enough that a job that has genuinely stopped is
    /// seen within one further period.
    /// </remarks>
    public double HeartbeatToleranceFactor { get; set; } = 2;

    /// <summary>
    /// Gets or sets what is added to the derived tolerance on top of the factor. Defaults to one
    /// minute.
    /// </summary>
    /// <remarks>
    /// The dispatcher polls on an interval of its own and a run takes as long as it takes, so a job on
    /// a very short cadence is routinely a few seconds later than its grid without anything being
    /// wrong. Without a floor like this such a job would sit at overdue permanently.
    /// </remarks>
    public TimeSpan HeartbeatGrace { get; set; } = TimeSpan.FromMinutes(1);
}
