using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Filtering;

/// <summary>Runs the filter transport bounds through the options infrastructure.</summary>
/// <remarks>
/// The rules live on <see cref="BsftFilterTransportOptions"/> itself, where a failure is stated as an
/// exception naming the member and what is wrong with it, and <see cref="IValidateOptions{TOptions}"/>
/// wants a result. This type is where the two meet, so a bound section is held to the same bounds as a
/// value passed in code.
/// </remarks>
internal sealed class BsftFilterTransportOptionsValidator : IValidateOptions<BsftFilterTransportOptions>
{
    /// <summary>Validates one configured instance.</summary>
    /// <param name="name">The named instance, unused: this package has one.</param>
    /// <param name="options">The instance to validate.</param>
    /// <returns>Success, or a failure naming the member.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftFilterTransportOptions options)
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
