using System.Globalization;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling;

/// <summary>
/// Runs the rules of <see cref="BsftJobOptions"/> under <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// The failures are collected rather than thrown one at a time, and each one names the job, because
/// these options are named per job: a host with a dozen jobs otherwise learns about one broken section
/// per restart without being told which job it belongs to.
/// </remarks>
internal sealed class BsftJobOptionsValidator : IValidateOptions<BsftJobOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftJobOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var job = string.IsNullOrEmpty(name) ? "(unnamed)" : name;
        var failures = new List<string>();

        // Zero would call the job overdue the instant it completed a run, so the check would never be
        // healthy for it; a negative tolerance is the same thing further away from making sense.
        if (options.HeartbeatTolerance is { } tolerance && tolerance <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(BsftJobOptions.HeartbeatTolerance)} of job '{job}' must be positive where it is set, but it is {tolerance}.");
        }

        // Below one the tolerance is shorter than the job's own interval, so a job that is running
        // exactly on time is reported overdue between two of its occurrences.
        // NaN and the infinities are rejected explicitly, because every comparison against NaN is false
        // and it would pass the test below and then make every derived tolerance meaningless.
        if (!double.IsFinite(options.HeartbeatToleranceFactor) || options.HeartbeatToleranceFactor < 1)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{nameof(BsftJobOptions.HeartbeatToleranceFactor)} of job '{job}' must be at least 1, but it is {options.HeartbeatToleranceFactor}."));
        }

        if (options.HeartbeatGrace < TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(BsftJobOptions.HeartbeatGrace)} of job '{job}' must not be negative, but it is {options.HeartbeatGrace}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
