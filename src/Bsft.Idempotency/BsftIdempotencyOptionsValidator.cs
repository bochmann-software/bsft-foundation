using Microsoft.Extensions.Options;

namespace Bsft.Idempotency;

/// <summary>
/// Validates <see cref="BsftIdempotencyOptions"/> at startup, so a combination that would silently
/// leave endpoints unprotected is a host that does not start.
/// </summary>
internal sealed class BsftIdempotencyOptionsValidator : IValidateOptions<BsftIdempotencyOptions>
{
    /// <summary>The widest key the framework's stores accept, which is the shared store's column.</summary>
    private const int KeyLengthCeiling = IdempotencyDefaults.MaxSupportedKeyLength;

    /// <summary>The lowest and highest values that are HTTP status codes at all.</summary>
    private const int LowestStatusCode = 100;

    /// <summary>The highest value that is an HTTP status code at all.</summary>
    private const int HighestStatusCode = 599;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftIdempotencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateHeaderAndKey(options, failures);
        ValidateWindows(options, failures);
        ValidateCollections(options, failures);

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    /// <summary>Checks the header name and the bounds on the key and the body.</summary>
    /// <param name="options">The options being validated.</param>
    /// <param name="failures">The list failures are appended to.</param>
    private static void ValidateHeaderAndKey(BsftIdempotencyOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.HeaderName))
            failures.Add($"{nameof(BsftIdempotencyOptions.HeaderName)} must not be blank.");

        if (options.MaxKeyLength is < 1 or > KeyLengthCeiling)
        {
            failures.Add(
                $"{nameof(BsftIdempotencyOptions.MaxKeyLength)} must be between 1 and {KeyLengthCeiling}.");
        }

        if (options.MaxBodyBytes < 1)
            failures.Add($"{nameof(BsftIdempotencyOptions.MaxBodyBytes)} must be positive.");

        if (options.MaxResponseBytes < 1)
            failures.Add($"{nameof(BsftIdempotencyOptions.MaxResponseBytes)} must be positive.");
    }

    /// <summary>Checks the three time windows and the one relationship that has to hold between them.</summary>
    /// <param name="options">The options being validated.</param>
    /// <param name="failures">The list failures are appended to.</param>
    private static void ValidateWindows(BsftIdempotencyOptions options, List<string> failures)
    {
        if (options.Retention <= TimeSpan.Zero)
            failures.Add($"{nameof(BsftIdempotencyOptions.Retention)} must be positive.");

        if (options.InProgressTimeout <= TimeSpan.Zero)
            failures.Add($"{nameof(BsftIdempotencyOptions.InProgressTimeout)} must be positive.");

        if (options.InProgressRetryAfter <= TimeSpan.Zero)
            failures.Add($"{nameof(BsftIdempotencyOptions.InProgressRetryAfter)} must be positive.");

        // A reservation that could outlive the replay window would be pruned while it still decides
        // whether a concurrent request is refused, which reopens the double-submit the reservation
        // exists to close — silently, because the second request simply looks new.
        if (options.Retention > TimeSpan.Zero
            && options.InProgressTimeout > TimeSpan.Zero
            && options.InProgressTimeout > options.Retention)
        {
            const string Consequence =
                ": a reservation outliving the replay window would be purged while it is still "
                + "refusing concurrent requests.";

            failures.Add(
                $"{nameof(BsftIdempotencyOptions.InProgressTimeout)} must not exceed {nameof(BsftIdempotencyOptions.Retention)}{Consequence}");
        }
    }

    /// <summary>Checks the three configurable lists.</summary>
    /// <param name="options">The options being validated.</param>
    /// <param name="failures">The list failures are appended to.</param>
    private static void ValidateCollections(BsftIdempotencyOptions options, List<string> failures)
    {
        if (options.Methods.Count == 0)
            failures.Add($"{nameof(BsftIdempotencyOptions.Methods)} must name at least one method.");

        if (options.Methods.Any(string.IsNullOrWhiteSpace))
            failures.Add($"{nameof(BsftIdempotencyOptions.Methods)} must not contain a blank method.");

        if (options.CachedResponseHeaders.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add(
                $"{nameof(BsftIdempotencyOptions.CachedResponseHeaders)} must not contain a blank header name.");
        }

        if (options.NonReplayableStatusCodes.Any(code => code is < LowestStatusCode or > HighestStatusCode))
        {
            failures.Add(
                $"{nameof(BsftIdempotencyOptions.NonReplayableStatusCodes)} must contain HTTP status codes "
                + $"between {LowestStatusCode} and {HighestStatusCode}.");
        }
    }
}
