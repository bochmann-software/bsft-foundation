using Microsoft.Extensions.Options;

namespace Bsft.Schema;

/// <summary>Refuses bounds that would not bound anything.</summary>
/// <remarks>
/// Every one of these is a ceiling on an untrusted input, so a zero or a negative is not a permissive
/// setting but an absent one. A host that meant to lift a limit raises it; a host that typed a zero
/// finds out at start rather than at the first submitted manifest.
/// </remarks>
internal sealed class BsftSchemaOptionsValidator : IValidateOptions<BsftSchemaOptions>
{
    /// <summary>Validates one named instance of the options.</summary>
    /// <param name="name">The options name, unused: this package registers only the default instance.</param>
    /// <param name="options">The options to validate.</param>
    /// <returns>Success, or a failure naming every bound that cannot be used.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        Require(errors, options.MaxDepth, nameof(options.MaxDepth));
        Require(errors, options.MaxProperties, nameof(options.MaxProperties));
        Require(errors, options.MaxSchemaNodes, nameof(options.MaxSchemaNodes));
        Require(errors, options.MaxArrayItems, nameof(options.MaxArrayItems));

        if (options.PatternTimeout <= TimeSpan.Zero)
            errors.Add($"{nameof(options.PatternTimeout)} must be greater than zero.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Require(List<string> errors, int value, string member)
    {
        if (value <= 0)
            errors.Add($"{member} must be greater than zero.");
    }
}
