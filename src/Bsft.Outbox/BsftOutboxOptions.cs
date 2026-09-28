namespace Bsft.Outbox;

/// <summary>
/// What one outbox's drain pass is bounded by: how much it claims at a time, how long it holds it,
/// and how a failed row is waited out before it is retired.
/// </summary>
/// <remarks>
/// Resolved per outbox as named options, so a host that runs a second outbox configures a second
/// name instead of inventing a second configuration shape. <see cref="SectionName"/> names the
/// parent section and the outbox's name is the sub-key underneath it, because how many outboxes a
/// host runs and what they are called is a runtime decision no single fixed section can express.
/// </remarks>
public sealed class BsftOutboxOptions
{
    /// <summary>The parent section one outbox's options are bound from, under its own name.</summary>
    public const string SectionName = "Bsft:Outbox:Instances";

    /// <summary>
    /// Gets or sets the delay between passes when the outbox is empty. Defaults to five seconds. It
    /// has to be positive: without a pause an idle drainer would query the store in a tight loop.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of rows claimed per pass. Defaults to 100. The whole batch is
    /// leased until one instant and applied one row at a time, so this value and
    /// <see cref="LeaseDuration"/> together fix the time a single side effect may take.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets how long a claim on a row holds. Defaults to one minute. A drainer that dies
    /// mid-pass blocks its rows for at most this long; set it above the time the slowest side effect
    /// needs, or another instance will apply the same row while the first is still busy.
    /// </summary>
    /// <remarks>
    /// It also has to cover a whole batch, not one row: a pass leases every claimed row until the same
    /// instant and then applies them sequentially. Getting it wrong costs throughput rather than
    /// correctness — the pass stops once the batch lease has run out instead of applying under a lease
    /// it no longer holds, and the untouched remainder is claimed again by the next pass. The
    /// combination is deliberately not validated: the duration that would decide the threshold is a
    /// property of the side effect, which this package cannot see.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets how often a row may fail before it is retired to the dead-letter state. Defaults
    /// to 10. Without a limit, one row whose side effect can never succeed would be attempted on every
    /// pass for the lifetime of the application. The retired row is kept, so nothing is lost.
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 10;

    /// <summary>
    /// Gets or sets the first interval a failed row is held back for. Defaults to five seconds and
    /// doubles with every further failure, up to <see cref="MaxRetryDelay"/>. It must not be negative;
    /// <see cref="TimeSpan.Zero"/> retries on the next pass.
    /// </summary>
    /// <remarks>
    /// The wait is recorded on the row, so it costs nothing: the row is simply not handed out again
    /// until it has passed, and the drainer spends the interval on the rest of the outbox. It exists
    /// because a fixed retry spends the attempt budget faster than anything it waits for can recover.
    /// </remarks>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the ceiling of the retry schedule. Defaults to five minutes. It caps the doubling
    /// of <see cref="RetryBackoff"/> and clamps what a failure may ask for, so it answers "how long
    /// can one row be held back" on its own. It has to be positive.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}
