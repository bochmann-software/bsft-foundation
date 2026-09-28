namespace Bsft.Core;

/// <summary>
/// Represents the result of a startup validation check.
/// </summary>
public sealed class StartupValidationResult
{
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];

    /// <summary>
    /// Gets a value indicating whether the validation passed.
    /// </summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// Gets the read-only list of validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Gets the read-only list of validation warnings. A warning states a finding the host should
    /// look at without failing startup over it, so a validator reporting one still passes and the
    /// host runner is the single place the finding surfaces.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Gets an optional summary message describing what was validated.
    /// </summary>
    public string? Message { get; private set; }

    /// <summary>
    /// Creates a successful validation result with a summary message.
    /// </summary>
    /// <param name="message">A summary of what was validated.</param>
    /// <returns>A valid <see cref="StartupValidationResult"/>.</returns>
    public static StartupValidationResult Success(string? message = null) => new() { Message = message };

    /// <summary>
    /// Creates a successful validation result carrying warnings. The result stays valid: a warning
    /// is a finding the validator reports, never a reason to stop the application.
    /// </summary>
    /// <param name="message">A summary of what was validated.</param>
    /// <param name="warnings">The findings to surface at warning level.</param>
    /// <returns>A valid <see cref="StartupValidationResult"/> carrying the warnings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="warnings"/> is null.</exception>
    public static StartupValidationResult Success(string? message, IReadOnlyList<string> warnings)
    {
        if (warnings is null)
            throw new ArgumentNullException(nameof(warnings));

        var result = new StartupValidationResult { Message = message };
        result._warnings.AddRange(warnings);
        return result;
    }

    /// <summary>
    /// Creates a failed validation result with the specified errors.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    /// <returns>An invalid <see cref="StartupValidationResult"/>.</returns>
    public static StartupValidationResult Failure(params string[] errors)
    {
        var result = new StartupValidationResult();
        result._errors.AddRange(errors);
        return result;
    }

    /// <summary>
    /// Creates a failed validation result that still carries its warnings.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    /// <param name="warnings">The findings to surface at warning level alongside them.</param>
    /// <returns>An invalid <see cref="StartupValidationResult"/> carrying both.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// A validator that found both has to report both: dropping the warnings because something else
    /// failed would hide a finding the host has no other way of learning about, and the failure it is
    /// reported beside is usually the one being fixed first.
    /// </remarks>
    public static StartupValidationResult Failure(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        if (errors is null)
            throw new ArgumentNullException(nameof(errors));
        if (warnings is null)
            throw new ArgumentNullException(nameof(warnings));

        var result = new StartupValidationResult();
        result._errors.AddRange(errors);
        result._warnings.AddRange(warnings);
        return result;
    }
}
