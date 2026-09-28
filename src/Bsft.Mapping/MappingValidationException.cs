namespace Bsft.Mapping;

/// <summary>
/// Exception thrown when mapping validation detects unmapped destination properties
/// that are not explicitly configured or ignored.
/// </summary>
public sealed class MappingValidationException : Exception
{
    /// <summary>
    /// Gets the list of validation error messages describing each unmapped property.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MappingValidationException"/> class.
    /// </summary>
    /// <param name="errors">The validation errors describing unmapped properties.</param>
    public MappingValidationException(IReadOnlyList<string> errors)
        : base("Mapping validation failed. Unmapped destination properties detected:\n" +
               string.Join("\n", errors))
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }
}
