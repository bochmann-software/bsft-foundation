namespace Bsft.Retention;

/// <summary>
/// What an operator decides about one retention pass: how long a record is kept, how much is deleted
/// at a time, how hard the sweep leans on the store, and how far its first sweep is offset from every
/// other pass in the deployment.
/// </summary>
/// <remarks>
/// These options are registered as <em>named</em> options, keyed by the pass type's full name, so a
/// host that runs several retention passes configures each one independently. The configuration
/// overload of <c>AddBsftRetention</c> binds <c>Bsft:Retention:&lt;pass type full name&gt;</c> for
/// that reason.
/// </remarks>
public sealed class BsftRetentionOptions
{
    /// <summary>The configuration section the per-pass sections sit under.</summary>
    public const string SectionName = "Bsft:Retention";

    /// <summary>
    /// The name the options of <typeparamref name="TPass"/> are registered under, which is also the
    /// per-pass configuration section under <see cref="SectionName"/> and the seed of its startup
    /// stagger.
    /// </summary>
    /// <typeparam name="TPass">The retention pass.</typeparam>
    /// <returns>The pass type's full name, or its short name where the runtime gives no full one.</returns>
    /// <remarks>
    /// Public because two callers outside the sweeper need the same answer: a host writing the
    /// per-pass configuration section, and a package that binds its own options onto the pass it
    /// ships. Deriving it by hand in either place would be a string that silently stops matching when
    /// the type moves.
    /// </remarks>
    public static string NameFor<TPass>()
        where TPass : class, IRetentionPass
        => typeof(TPass).FullName ?? typeof(TPass).Name;

    /// <summary>
    /// Gets or sets how long a record is kept before it may be purged. Defaults to <c>null</c>, which
    /// keeps every record indefinitely and runs no sweep at all.
    /// </summary>
    /// <remarks>
    /// Null rather than a default window on purpose: deleting an application's history is a decision
    /// a host makes explicitly, and a framework that guessed a window would be deleting data on the
    /// strength of that guess. "Off" needs no second flag — leaving this unset is off.
    /// </remarks>
    public TimeSpan? RetentionPeriod { get; set; }

    /// <summary>Gets or sets the most records deleted per batch. Defaults to 500.</summary>
    /// <remarks>
    /// The cap is what keeps a first sweep against a large backlog from turning into a single
    /// long-locking transaction, and it is also the signal the sweep reads: a full batch means more
    /// is waiting, a short one means the backlog is drained.
    /// </remarks>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets how long the sweep pauses between two batches. Defaults to zero, which drains as
    /// fast as the store allows.
    /// </summary>
    /// <remarks>
    /// Pacing exists for the store, not for the sweep: a backlog of millions of rows deleted flat out
    /// can starve the transactional workload sharing that database. A pause of a second or two spreads
    /// the same delete over a longer window at no cost to anything but how quickly the backlog clears.
    /// </remarks>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets the longest a sweep waits for the next one, however long the retention is.
    /// Defaults to one hour.
    /// </summary>
    /// <remarks>
    /// The effective interval is the smaller of this and <see cref="RetentionPeriod"/>: sweeping at
    /// the retention itself keeps a short retention honest without a second option to configure, and
    /// this cap keeps a retention of months from meaning one enormous delete every few months. A
    /// record therefore outlives its retention by at most one interval.
    /// </remarks>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the window the deterministic startup stagger spreads the first sweep across.
    /// Defaults to one minute; zero starts every pass immediately.
    /// </summary>
    /// <remarks>
    /// Every retention pass in a deployment is registered at the same startup and would otherwise
    /// take its first sweep at the same instant, so a host running a dozen of them opens with a dozen
    /// simultaneous bulk deletes against one database. The offset is derived from the pass name
    /// rather than drawn at random, so a given pass lands at the same point of the window on every
    /// instance and every restart — which keeps the spread reproducible when something has to be
    /// diagnosed.
    /// </remarks>
    public TimeSpan MaxStartupStagger { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets how many batches one sweep may delete before it yields. Defaults to 100.
    /// </summary>
    /// <remarks>
    /// A bound rather than an unbounded drain, so a sweep that meets a very large backlog still comes
    /// back round through the loop's failure handling and its stopping token instead of sitting in one
    /// pass indefinitely. Hitting the bound is not an error: the sweep reports that work remains and
    /// the next one continues where it left off.
    /// </remarks>
    public int MaxBatchesPerSweep { get; set; } = 100;
}
