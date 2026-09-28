using Microsoft.Extensions.Options;

namespace Bsft.Scheduling;

/// <summary>
/// Runs the rules of <see cref="BsftSchedulingOptions"/> under <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// An <see cref="IValidateOptions{TOptions}"/> rather than data annotations because most of what
/// matters here is a <see cref="TimeSpan"/> that has to be positive, which no annotation expresses,
/// and because every failure is collected rather than thrown one at a time: a misconfigured host is
/// told everything that is wrong with its section in one go instead of over several restarts. The
/// alternative — failing inside the dispatcher's first poll — is a scheduler that quietly does
/// nothing.
/// </remarks>
internal sealed class BsftSchedulingOptionsValidator : IValidateOptions<BsftSchedulingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftSchedulingOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();

        // A non-positive batch size makes every poll return nothing, so no job would ever run.
        if (options.BatchSize < 1)
            failures.Add($"{nameof(BsftSchedulingOptions.BatchSize)} must be at least 1, but it is {options.BatchSize}.");

        if (options.MaxAttempts < 1)
            failures.Add($"{nameof(BsftSchedulingOptions.MaxAttempts)} must be at least 1, but it is {options.MaxAttempts}.");

        // Zero is rejected, not just negatives: an empty pass would come straight back round and the
        // dispatcher would hammer the store with no backoff at all. A negative one makes the delay
        // throw and takes the host down with it.
        if (options.PollInterval <= TimeSpan.Zero)
            failures.Add($"{nameof(BsftSchedulingOptions.PollInterval)} must be positive, but it is {options.PollInterval}.");

        // Zero would turn the re-seed into a tight loop over the registrations, hammering the store
        // with the existence check of every recurring job and never sleeping.
        if (options.RecurringSeedInterval <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(BsftSchedulingOptions.RecurringSeedInterval)} must be positive, but it is {options.RecurringSeedInterval}.");
        }

        if (options.RetryDelay < TimeSpan.Zero)
            failures.Add($"{nameof(BsftSchedulingOptions.RetryDelay)} must not be negative, but it is {options.RetryDelay}.");

        if (options.MaxCatchUpAge < TimeSpan.Zero)
            failures.Add($"{nameof(BsftSchedulingOptions.MaxCatchUpAge)} must not be negative, but it is {options.MaxCatchUpAge}.");

        // A non-positive lease expires the moment it is taken, so every run would immediately look
        // abandoned and every instance would execute every occurrence.
        if (options.LeaseDuration <= TimeSpan.Zero)
            failures.Add($"{nameof(BsftSchedulingOptions.LeaseDuration)} must be positive, but it is {options.LeaseDuration}.");

        if (string.IsNullOrWhiteSpace(options.InstanceId))
            failures.Add($"{nameof(BsftSchedulingOptions.InstanceId)} must not be empty.");

        // Zero would turn the stored-schedule seed into a tight loop hammering the store with the same
        // check RecurringSeedInterval guards against for the code-registered catalog.
        if (options.StoredScheduleSeedInterval <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(BsftSchedulingOptions.StoredScheduleSeedInterval)} must be positive, but it is {options.StoredScheduleSeedInterval}.");
        }

        // A non-positive batch size makes every stored-schedule pass return nothing, so no persisted
        // schedule would ever run.
        if (options.StoredScheduleBatchSize < 1)
        {
            failures.Add(
                $"{nameof(BsftSchedulingOptions.StoredScheduleBatchSize)} must be at least 1, but it is {options.StoredScheduleBatchSize}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
