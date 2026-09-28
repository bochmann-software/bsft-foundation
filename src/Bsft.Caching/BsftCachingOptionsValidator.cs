using Microsoft.Extensions.Options;

namespace Bsft.Caching;

/// <summary>
/// Validates <see cref="BsftCachingOptions"/> at startup.
/// </summary>
/// <remarks>
/// The rules here used to be an <see cref="ArgumentOutOfRangeException"/> thrown from
/// <c>AddBsftCaching</c>. Moving them into a validator means the host fails with every complaint at
/// once rather than one restart at a time, and that a value bound from configuration is checked at
/// all — the registration-time throw only ever saw what the <c>Action</c> delegate set.
/// </remarks>
internal sealed class BsftCachingOptionsValidator : IValidateOptions<BsftCachingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftCachingOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();

        if (options.DefaultTimeToLive <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(BsftCachingOptions.DefaultTimeToLive)} must be positive, but was {options.DefaultTimeToLive}.");
        }

        if (!Enum.IsDefined(options.Provider))
        {
            failures.Add(
                $"{nameof(BsftCachingOptions.Provider)} '{options.Provider}' is not a known cache provider.");
        }

        if (string.IsNullOrWhiteSpace(options.KeyPrefix))
        {
            failures.Add($"{nameof(BsftCachingOptions.KeyPrefix)} must not be empty: it is what keeps this framework's entries apart from other writers on a shared distributed cache.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
