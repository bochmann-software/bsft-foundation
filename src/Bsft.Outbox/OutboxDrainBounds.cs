namespace Bsft.Outbox;

/// <summary>The bounds a drain pass can actually work within.</summary>
/// <remarks>
/// Asked twice: once when the host starts, so a misconfigured outbox is refused before anything runs,
/// and once by the drainer's constructor, because it is public and a caller can hand it options that
/// never passed through the options pipeline. One rule set answers both, so the two can never
/// disagree about what a workable outbox is.
/// </remarks>
internal static class OutboxDrainBounds
{
    /// <summary>
    /// Rejects options that would break the pass silently or loudly. A non-positive batch size makes
    /// every pass claim nothing, so the outbox would simply never drain; a non-positive poll interval
    /// turns an idle loop into a tight query loop, or makes the delay throw; a non-positive lease
    /// expires the moment it is taken, which hands every row to every instance at once; an attempt
    /// limit below one would retire every row on its first failure; a negative backoff would put every
    /// retry instant in the past, and a ceiling of zero or less would clamp every wait — including one
    /// a failure asked for — back to nothing.
    /// </summary>
    /// <param name="options">The bounds under test.</param>
    /// <returns>The same options, once nothing about them is out of range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the batch size is below one, the poll interval is negative, the lease duration is
    /// not positive, the attempt limit is below one, the retry backoff is negative or the retry
    /// ceiling is not positive.
    /// </exception>
    internal static BsftOutboxOptions EnsureWorkable(BsftOutboxOptions options)
    {
        if (options.BatchSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(BsftOutboxOptions.BatchSize), options.BatchSize, "The batch size must be at least 1.");
        if (options.PollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(BsftOutboxOptions.PollInterval), options.PollInterval, "The poll interval must be positive.");
        if (options.LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(BsftOutboxOptions.LeaseDuration),
                options.LeaseDuration,
                "The lease duration must be positive.");
        if (options.MaxDeliveryAttempts < 1)
            throw new ArgumentOutOfRangeException(
                nameof(BsftOutboxOptions.MaxDeliveryAttempts),
                options.MaxDeliveryAttempts,
                "The maximum number of delivery attempts must be at least 1.");
        if (options.RetryBackoff < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(BsftOutboxOptions.RetryBackoff),
                options.RetryBackoff,
                "The retry backoff must not be negative.");
        if (options.MaxRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(BsftOutboxOptions.MaxRetryDelay),
                options.MaxRetryDelay,
                "The retry ceiling must be positive.");

        return options;
    }
}
