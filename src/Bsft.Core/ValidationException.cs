namespace Bsft.Core;

/// <summary>
/// Exception thrown when one or more validation errors occur.
/// Carries a dictionary of field-level validation errors.
/// </summary>
public sealed class ValidationException : BsftException
{
    /// <summary>The code every instance carries unless a caller names its own.</summary>
    private const string DefaultErrorCode = "VALIDATION_FAILED";

    /// <summary>
    /// Gets the validation errors keyed by field name.
    /// </summary>
    /// <remarks>
    /// Read-only because the errors are the reason the exception exists: a handler that could add to
    /// or clear them after the throw would change what the caller is told went wrong, and the problem
    /// details written from them further up would no longer describe the failure that happened.
    /// </remarks>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class with the specified errors.
    /// </summary>
    /// <param name="errors">The validation errors keyed by field name.</param>
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.", DefaultErrorCode)
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class with a custom message and errors.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errors">The validation errors keyed by field name.</param>
    public ValidationException(string message, IReadOnlyDictionary<string, string[]> errors)
        : base(message, DefaultErrorCode)
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }
}
