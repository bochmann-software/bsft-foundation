using Microsoft.Extensions.Options;

namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Validates <see cref="BsftSagaOptions"/> at startup.
/// </summary>
/// <remarks>
/// The rules mirror the guards <see cref="Timeouts.SagaTimeoutDispatcher"/> applies to its own constructor
/// arguments, deliberately: the dispatcher keeps them because it can be constructed directly, and this
/// validator is what turns them into a startup failure with every complaint at once rather than a
/// background service that throws on its first pass. Neither is stricter than the other.
/// </remarks>
internal sealed class BsftSagaOptionsValidator : IValidateOptions<BsftSagaOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftSagaOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var timeouts = options.Timeouts;
        var failures = new List<string>();

        if (timeouts.PollInterval < TimeSpan.Zero)
        {
            failures.Add(
                $"Timeouts.{nameof(timeouts.PollInterval)} must not be negative, but was {timeouts.PollInterval}.");
        }

        if (timeouts.BatchSize < 1)
        {
            failures.Add($"Timeouts.{nameof(timeouts.BatchSize)} must be at least 1, but was {timeouts.BatchSize}.");
        }

        if (timeouts.MaxCatchUpAge < TimeSpan.Zero)
        {
            failures.Add(
                $"Timeouts.{nameof(timeouts.MaxCatchUpAge)} must not be negative, but was {timeouts.MaxCatchUpAge}.");
        }

        if (timeouts.MaxDispatchAttempts < 1)
        {
            failures.Add(
                $"Timeouts.{nameof(timeouts.MaxDispatchAttempts)} must be at least 1, but was {timeouts.MaxDispatchAttempts}. Without a limit one timeout that can never fire keeps its place at the head of every poll.");
        }

        if (!Enum.IsDefined(timeouts.CatchUpPolicy))
        {
            failures.Add(
                $"Timeouts.{nameof(timeouts.CatchUpPolicy)} '{timeouts.CatchUpPolicy}' is not a known catch-up policy.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
