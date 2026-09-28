using Microsoft.Extensions.Options;

namespace Bsft.Data;

/// <summary>Runs the data-layer option rules through the options infrastructure.</summary>
/// <remarks>
/// The rules live on <see cref="BsftDataOptions"/> itself, where they state a failure as an exception
/// carrying the member and the offending value. That is the shape the registration call wants and
/// <see cref="IValidateOptions{TOptions}"/> wants a result, so this type is where the two meet.
/// </remarks>
internal sealed class BsftDataOptionsValidator : IValidateOptions<BsftDataOptions>
{
    /// <summary>Validates one configured instance.</summary>
    /// <param name="name">The named instance, unused: this package has one.</param>
    /// <param name="options">The instance to validate.</param>
    /// <returns>Success, or a failure naming the member and the value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftDataOptions options)
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
