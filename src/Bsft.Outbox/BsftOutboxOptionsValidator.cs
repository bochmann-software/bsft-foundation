using Microsoft.Extensions.Options;

namespace Bsft.Outbox;

/// <summary>Refuses an outbox whose bounds a drain pass cannot work within, while the host starts.</summary>
/// <remarks>
/// The rules themselves live in <see cref="OutboxDrainBounds"/>, which the drainer's constructor asks
/// too. What this adds is when the answer arrives: at start rather than at the first pass, so a
/// deployment with a zero batch size does not come up and then quietly never drain.
/// </remarks>
internal sealed class BsftOutboxOptionsValidator : IValidateOptions<BsftOutboxOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftOutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            OutboxDrainBounds.EnsureWorkable(options);
        }
        catch (ArgumentOutOfRangeException outOfRange)
        {
            return ValidateOptionsResult.Fail(
                name is null
                    ? outOfRange.Message
                    : $"The outbox named '{name}' is misconfigured. {outOfRange.Message}");
        }

        return ValidateOptionsResult.Success;
    }
}
