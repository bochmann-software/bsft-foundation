using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Cors;

/// <summary>Runs the CORS option rules through the options infrastructure.</summary>
/// <remarks>
/// The rules live on <see cref="BsftCorsOptions"/> itself, where a failure is stated as an exception
/// carrying the offending value, and <see cref="IValidateOptions{TOptions}"/> wants a result. This type
/// is where the two meet.
/// </remarks>
internal sealed class BsftCorsOptionsValidator : IValidateOptions<BsftCorsOptions>
{
    /// <summary>Validates one configured instance.</summary>
    /// <param name="name">The named instance, unused: this package has one.</param>
    /// <param name="options">The instance to validate.</param>
    /// <returns>Success, or a failure naming the offending value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftCorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            options.Validate();
        }
        catch (InvalidOperationException failure)
        {
            return ValidateOptionsResult.Fail(failure.Message);
        }

        return ValidateOptionsResult.Success;
    }
}
