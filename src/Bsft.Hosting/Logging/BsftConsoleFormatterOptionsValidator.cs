using Bsft.Logging.Sinks;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Logging;

/// <summary>Applies the file logger's rules for persisted scope keys to the console formatter's options.</summary>
/// <remarks>
/// The two sinks take the same list, so a list the file logger refuses at startup is refused here too, rather than
/// being shown by the console and rejected by the file.
/// </remarks>
internal sealed class BsftConsoleFormatterOptionsValidator : IValidateOptions<BsftConsoleFormatterOptions>
{
    private readonly IScopeLineEncoder _encoder;

    /// <summary>Initializes a new instance of the <see cref="BsftConsoleFormatterOptionsValidator"/> class.</summary>
    /// <param name="encoder">The encoder that knows the key rules.</param>
    public BsftConsoleFormatterOptionsValidator(IScopeLineEncoder encoder)
    {
        _encoder = encoder;
    }

    /// <summary>Validates one configured instance.</summary>
    /// <param name="name">The named instance, unused.</param>
    /// <param name="options">The instance to validate.</param>
    /// <returns>Success, or a failure naming the member and the value.</returns>
    public ValidateOptionsResult Validate(string? name, BsftConsoleFormatterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return _encoder.DescribeKeyProblem(options.PersistedScopeKeys) is { } problem
            ? ValidateOptionsResult.Fail($"{nameof(BsftConsoleFormatterOptions)}.{nameof(BsftConsoleFormatterOptions.PersistedScopeKeys)} {problem}.")
            : ValidateOptionsResult.Success;
    }
}
