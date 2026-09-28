namespace Bsft.Schema;

/// <summary>What checking one document against one schema produced.</summary>
/// <remarks>
/// Every refusal, not the first. A form with four wrong fields that reports one of them is a form
/// somebody submits four times.
/// </remarks>
public sealed class SchemaValidationResult
{
    /// <summary>Gets every refusal, empty when the document matches.</summary>
    public required IReadOnlyList<SchemaViolation> Violations { get; init; }

    /// <summary>Gets a value indicating whether the document matches the schema.</summary>
    public bool IsValid => Violations.Count == 0;
}
