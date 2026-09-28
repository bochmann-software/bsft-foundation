using System.Collections.ObjectModel;

namespace Bsft.Core;

/// <summary>
/// Represents the result of a validation operation.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the validation passed.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the read-only dictionary of validation errors keyed by field name.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationResult"/> class with no errors.
    /// </summary>
    public ValidationResult()
    {
        Errors = ReadOnlyDictionary<string, string[]>.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationResult"/> class with the specified errors.
    /// </summary>
    /// <param name="errors">The validation errors keyed by field name.</param>
    public ValidationResult(IDictionary<string, string[]> errors)
    {
        if (errors is null)
            throw new ArgumentNullException(nameof(errors));

        // Defensive copy so the result cannot be mutated through the caller's reference after creation.
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors));
    }

    /// <summary>
    /// Creates a successful validation result with no errors.
    /// </summary>
    /// <returns>A new <see cref="ValidationResult"/> representing success.</returns>
    public static ValidationResult Success() => new();

    /// <summary>
    /// Creates a failed validation result with the specified errors.
    /// </summary>
    /// <param name="errors">The validation errors keyed by field name.</param>
    /// <returns>A new <see cref="ValidationResult"/> representing failure.</returns>
    public static ValidationResult Failure(IDictionary<string, string[]> errors) => new(errors);
}
