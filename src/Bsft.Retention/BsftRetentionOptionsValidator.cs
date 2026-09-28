using Microsoft.Extensions.Options;

namespace Bsft.Retention;

/// <summary>
/// Runs the rules of <see cref="BsftRetentionOptions"/> under <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// An <see cref="IValidateOptions{TOptions}"/> rather than data annotations: most of what matters
/// is a <see cref="TimeSpan"/> that has to be positive,
/// which no annotation expresses, and every failure is collected so a misconfigured host is told
/// everything at once. The name of the pass is carried into each message, because these are named
/// options and "BatchSize must be at least 1" is not actionable when a host runs six passes.
/// </remarks>
internal sealed class BsftRetentionOptionsValidator : IValidateOptions<BsftRetentionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftRetentionOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var pass = string.IsNullOrEmpty(name) ? nameof(BsftRetentionOptions) : name;
        var failures = new List<string>();

        // Zero would delete records the instant they were written, which is not a retention policy
        // anyone configures on purpose; null is how a pass is turned off.
        if (options.RetentionPeriod is { } retention && retention <= TimeSpan.Zero)
        {
            failures.Add(
                $"{pass}: {nameof(BsftRetentionOptions.RetentionPeriod)} must be positive when it is set, but it is {retention}.");
        }

        // A non-positive batch would delete nothing and, because a short batch ends the sweep, would
        // do it once per interval forever.
        if (options.BatchSize < 1)
            failures.Add($"{pass}: {nameof(BsftRetentionOptions.BatchSize)} must be at least 1, but it is {options.BatchSize}.");

        if (options.MaxBatchesPerSweep < 1)
        {
            failures.Add(
                $"{pass}: {nameof(BsftRetentionOptions.MaxBatchesPerSweep)} must be at least 1, but it is {options.MaxBatchesPerSweep}.");
        }

        // Zero is legitimate — it means drain as fast as the store allows — but a negative delay
        // makes the wait throw and takes the host down with it.
        if (options.BatchDelay < TimeSpan.Zero)
            failures.Add($"{pass}: {nameof(BsftRetentionOptions.BatchDelay)} must not be negative, but it is {options.BatchDelay}.");

        // Zero would turn the loop's wait into a tight spin over the store.
        if (options.SweepInterval <= TimeSpan.Zero)
        {
            failures.Add(
                $"{pass}: {nameof(BsftRetentionOptions.SweepInterval)} must be positive, but it is {options.SweepInterval}.");
        }

        // Zero is legitimate — every pass starts at once — but a negative window has no meaning and
        // would make the offset calculation throw.
        if (options.MaxStartupStagger < TimeSpan.Zero)
        {
            failures.Add(
                $"{pass}: {nameof(BsftRetentionOptions.MaxStartupStagger)} must not be negative, but it is {options.MaxStartupStagger}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
