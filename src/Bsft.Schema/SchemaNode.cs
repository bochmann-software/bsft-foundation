using System.Text.Json;

namespace Bsft.Schema;

/// <summary>One node of a schema that has already been checked to be inside the subset.</summary>
/// <remarks>
/// <para>
/// Produced only by <see cref="ISchemaReader"/>, and reaching a validator means every keyword
/// on it was understood, every bound was within the configured limits and every pattern compiled. So
/// the validator has no refusals of its own about the schema, only about the document: the two
/// failures are told apart by which call produced them, not by reading a code.
/// </para>
/// <para>
/// Objects carry <see cref="Properties"/> and <see cref="Required"/>; arrays carry
/// <see cref="Items"/>; the scalars carry the bounds that apply to them. A node never carries the
/// keywords of a kind it is not, because the reader refuses those rather than storing them.
/// </para>
/// </remarks>
public sealed class SchemaNode
{
    /// <summary>Gets the type this node declares.</summary>
    public required SchemaValueKind Kind { get; init; }

    /// <summary>Gets the declared format, or null. One of <see cref="SchemaFormats"/>.</summary>
    public string? Format { get; init; }

    /// <summary>Gets the declared pattern, already compiled, or null.</summary>
    /// <remarks>
    /// Compiled by the reader with backtracking off and a time limit, because the expression came from
    /// the same submitted document everything else here did.
    /// </remarks>
    public System.Text.RegularExpressions.Regex? Pattern { get; init; }

    /// <summary>Gets the only values this node admits, or null when it admits any of its type.</summary>
    public IReadOnlyList<JsonElement>? AllowedValues { get; init; }

    /// <summary>Gets the value a host may fall back to when the property is absent, or null.</summary>
    /// <remarks>
    /// Carried, never applied. Filling a default in is a decision about somebody's configuration, and
    /// a validator that silently supplied one would report a document valid that nobody submitted.
    /// </remarks>
    public JsonElement? Default { get; init; }

    /// <summary>Gets the shortest admissible string, or null.</summary>
    public int? MinLength { get; init; }

    /// <summary>Gets the longest admissible string, or null.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Gets the smallest admissible number, or null.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>Gets the largest admissible number, or null.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>Gets the fewest admissible array items, or null.</summary>
    public int? MinItems { get; init; }

    /// <summary>Gets the most admissible array items, or null.</summary>
    public int? MaxItems { get; init; }

    /// <summary>Gets the schema every array item is checked against, or null when this is not an array.</summary>
    public SchemaNode? Items { get; init; }

    /// <summary>Gets the declared properties, or null when this is not an object.</summary>
    public IReadOnlyDictionary<string, SchemaNode>? Properties { get; init; }

    /// <summary>Gets the properties that must be present, empty when none are.</summary>
    public IReadOnlyList<string> Required { get; init; } = [];
}
