namespace Bsft.Schema;

/// <summary>What reading a submitted schema produced: the schema, or why it was refused.</summary>
/// <remarks>
/// Never both. <see cref="Schema"/> is non-null exactly when <see cref="Violations"/> is empty, which
/// is what lets a caller hand the result straight to a validator without a second check of its own.
/// </remarks>
public sealed class SchemaReadResult
{
    /// <summary>Gets the schema, or <see langword="null"/> when it was refused.</summary>
    public SchemaNode? Schema { get; init; }

    /// <summary>Gets every reason it was refused, empty when it was not.</summary>
    public required IReadOnlyList<SchemaViolation> Violations { get; init; }

    /// <summary>Gets a value indicating whether the schema may be used.</summary>
    public bool IsUsable => Schema is not null;
}
