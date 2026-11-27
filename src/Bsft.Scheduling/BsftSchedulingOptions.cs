using System.Globalization;

namespace Bsft.Scheduling;

/// <summary>
/// What an operator decides about the job scheduler: how often it polls, how long a claim holds a
/// run, how often a failed run is retried and what happens to work that fell behind.
/// </summary>
public sealed class BsftSchedulingOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Scheduling";

    /// <summary>
    /// The largest value <see cref="MaxResultBytes"/> may be configured to: 64 KiB. The listing returns a
    /// result on every row of a page, under the metadata permission, so the ceiling keeps even a large
    /// page a summary rather than an export.
    /// </summary>
    public const int MaxResultBytesCeiling = 64 * 1024;

    private const int DefaultPollIntervalSeconds = 5;
    private const int DefaultSeedAndLeaseMinutes = 5;
    private const int DefaultRetryAndStoredSeedSeconds = 30;
    private const int DefaultBatchSize = 50;
    private const int DefaultMaxAttempts = 3;

    /// <summary>The default of <see cref="MaxResultBytes"/>: 4 KiB, ample for a handful of counters.</summary>
    public const int DefaultMaxResultBytes = 4 * 1024;

    /// <summary>Gets or sets how long the dispatcher waits after a pass that claimed nothing. Defaults to five seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(DefaultPollIntervalSeconds);

    /// <summary>Gets or sets the maximum number of claimable runs read per pass. Defaults to 50.</summary>
    public int BatchSize { get; set; } = DefaultBatchSize;

    /// <summary>
    /// Gets or sets how often the recurring registrations are re-seeded. Defaults to five minutes.
    /// </summary>
    /// <remarks>
    /// The schedule normally sustains itself, because every settled occurrence creates the next one.
    /// A crash between settling a run and writing its successor breaks that chain, and the job then
    /// has no pending occurrence at all. Re-seeding is what repairs it: seeding is idempotent, so a
    /// job that still has a live occurrence is left untouched and only a broken chain is restored.
    /// The interval is the worst-case silence of such a job, traded against a poll of the store.
    /// </remarks>
    public TimeSpan RecurringSeedInterval { get; set; } = TimeSpan.FromMinutes(DefaultSeedAndLeaseMinutes);

    /// <summary>
    /// Gets or sets how long a claim holds a run before another instance may take it over. It has to
    /// comfortably exceed the longest expected execution: a job still running when its lease lapses
    /// is executed a second time in parallel. Defaults to five minutes.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(DefaultSeedAndLeaseMinutes);

    /// <summary>
    /// Gets or sets how many times a run is attempted before it is marked failed. Defaults to three.
    /// A recurring job schedules its next occurrence regardless of whether the current one failed.
    /// </summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>Gets or sets how long a failed run waits before the next attempt. Defaults to thirty seconds.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(DefaultRetryAndStoredSeedSeconds);

    /// <summary>Gets or sets how overdue runs are handled. Defaults to <see cref="JobCatchUpPolicy.RunAll"/>.</summary>
    public JobCatchUpPolicy CatchUpPolicy { get; set; } = JobCatchUpPolicy.RunAll;

    /// <summary>
    /// Gets or sets how far overdue a run may be before <see cref="JobCatchUpPolicy.SkipBeyondMaxAge"/>
    /// discards it. Defaults to one day.
    /// </summary>
    public TimeSpan MaxCatchUpAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets the identifier recorded as the lease owner. It only has to distinguish the
    /// instances of one deployment from each other, and exists so an operator can tell which process
    /// is sitting on a stuck run. Defaults to the machine name plus a per-process suffix.
    /// </summary>
    public string InstanceId { get; set; } =
        string.Create(CultureInfo.InvariantCulture, $"{Environment.MachineName}:{Guid.NewGuid():N}");

    /// <summary>
    /// Gets or sets how often the persisted, per-tenant schedule catalog is polled for due occurrences.
    /// Defaults to thirty seconds.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RecurringSeedInterval"/> because the two catalogs answer different
    /// questions at a different cost: the code-registered one only re-seeds a chain that broke, while
    /// this one is the only clock a persisted schedule has, so it wants a tighter interval by default.
    /// </remarks>
    public TimeSpan StoredScheduleSeedInterval { get; set; } = TimeSpan.FromSeconds(DefaultRetryAndStoredSeedSeconds);

    /// <summary>
    /// Gets or sets the maximum number of due stored schedules read per pass. Defaults to 50.
    /// </summary>
    public int StoredScheduleBatchSize { get; set; } = DefaultBatchSize;

    /// <summary>
    /// Gets or sets the largest run result a job may record, measured in UTF-8 bytes of its JSON.
    /// Defaults to <see cref="DefaultMaxResultBytes"/>; at most <see cref="MaxResultBytesCeiling"/>.
    /// </summary>
    /// <remarks>
    /// A result is a summary such as a handful of counters, and the run listing returns it for every
    /// row of a page, so the bound keeps a page from turning into a bulk export. A job that records a
    /// larger value is refused in its own call with <see cref="Bsft.Core.PayloadTooLargeException"/>
    /// carrying <see cref="SchedulingErrorCodes.ResultTooLarge"/>, and nothing is recorded.
    /// </remarks>
    public int MaxResultBytes { get; set; } = DefaultMaxResultBytes;

    /// <summary>
    /// Gets or sets how long a settled run (completed, failed or skipped) is kept before the EF Core retention
    /// pass may delete it. Defaults to <c>null</c>, which keeps every run.
    /// </summary>
    /// <remarks>
    /// Off by default, because what a settled run is worth is the host's to say: it is the record an operator
    /// reads to find out what ran and why it failed. Only
    /// <c>AddBsftJobRunEFCoreRetention</c> reads it; the in-memory store ends with the process and ignores it.
    /// A run is aged by the instant it settled, and a pending or running run is never deleted.
    /// </remarks>
    public TimeSpan? JobRunRetention { get; set; }

    /// <summary>
    /// Gets or sets how many of the latest settled runs of each job the retention pass keeps whatever their
    /// age, so a rarely running job still has its last outcome for diagnostics. Defaults to zero, which
    /// keeps nothing beyond <see cref="JobRunRetention"/>.
    /// </summary>
    public int JobRunRetentionKeepLatest { get; set; }
}
