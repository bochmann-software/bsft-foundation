using System.Globalization;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.BackgroundWork;

/// <summary>
/// Runs the rules of <see cref="BsftRunOnceOptions"/> under <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// An <see cref="IValidateOptions{TOptions}"/> rather than data annotations because most of what
/// matters here is a <see cref="TimeSpan"/> that has to be positive, which no annotation expresses,
/// and because every failure is collected rather than thrown one at a time. The alternative — failing
/// inside the first backoff — is a startup operation that either spins or never comes back, hours
/// after the deployment that misconfigured it.
/// </remarks>
internal sealed class BsftRunOnceOptionsValidator : IValidateOptions<BsftRunOnceOptions>
{
    /// <summary>
    /// The longest delay <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> accepts,
    /// which is roughly 49.7 days.
    /// </summary>
    /// <remarks>
    /// Anything beyond it throws <see cref="ArgumentOutOfRangeException"/> from inside the loop rather
    /// than at startup, so a configuration nobody could have meant literally — a delay of years — takes
    /// the service down on its first backoff instead of being refused while the host is still coming up.
    /// </remarks>
    private static readonly TimeSpan LongestDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftRunOnceOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();

        if (options.StartupDelay < TimeSpan.Zero)
            failures.Add($"{nameof(BsftRunOnceOptions.StartupDelay)} must not be negative, but it is {options.StartupDelay}.");

        // Zero is rejected, not just negatives: a failing operation would come straight back round
        // with no backoff at all and hammer whatever it is that has not come up yet.
        if (options.InitialRetryDelay <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(BsftRunOnceOptions.InitialRetryDelay)} must be positive, but it is {options.InitialRetryDelay}.");
        }

        // A cap below the first delay would mean the very first backoff is already clamped, so the
        // configured initial delay is a value that never applies — which is the sort of contradiction
        // an operator finds out about by reading the log timestamps.
        if (options.MaxRetryDelay < options.InitialRetryDelay)
        {
            failures.Add(
                $"{nameof(BsftRunOnceOptions.MaxRetryDelay)} must not be shorter than {nameof(BsftRunOnceOptions.InitialRetryDelay)}, "
                + $"but it is {options.MaxRetryDelay} against {options.InitialRetryDelay}.");
        }

        // A factor below one shrinks the delay towards zero, so an operation whose dependency stays
        // down ends up retrying in a tight loop — the opposite of what backoff is for. NaN is rejected
        // here rather than left to the arithmetic, because every comparison against it is false: it
        // passes a "< 1" test and then turns the first backoff into a delay of no length at all.
        if (!double.IsFinite(options.BackoffFactor) || options.BackoffFactor < 1)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{nameof(BsftRunOnceOptions.BackoffFactor)} must be at least 1, but it is {options.BackoffFactor}."));
        }

        foreach (var (property, delay) in new[]
                 {
                     (nameof(BsftRunOnceOptions.StartupDelay), options.StartupDelay),
                     (nameof(BsftRunOnceOptions.InitialRetryDelay), options.InitialRetryDelay),
                     (nameof(BsftRunOnceOptions.MaxRetryDelay), options.MaxRetryDelay),
                 })
        {
            if (delay > LongestDelay)
                failures.Add($"{property} must not exceed {LongestDelay}, but it is {delay}.");
        }

        if (options.MaxAttempts < 0)
            failures.Add($"{nameof(BsftRunOnceOptions.MaxAttempts)} must not be negative, but it is {options.MaxAttempts}.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
