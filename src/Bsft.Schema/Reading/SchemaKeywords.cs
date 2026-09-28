using System.Collections.Frozen;

namespace Bsft.Schema.Reading;

/// <summary>The keywords the subset understands, per kind.</summary>
/// <remarks>
/// Written out rather than derived, because the list is the specification: what is not here is refused
/// by name, and adding a keyword is a deliberate edit to this file rather than a side effect of a
/// reader change.
/// </remarks>
internal static class SchemaKeywords
{
    /// <summary>Gets the empty set, for a kind that admits no keywords of its own.</summary>
    public static IReadOnlySet<string> None { get; } = Set();

    /// <summary>Gets what every node may carry and what constrains nothing.</summary>
    public static IReadOnlySet<string> Annotations { get; } = Set("$schema", "title", "description", "default");

    /// <summary>Gets what is valid on any node.</summary>
    public static IReadOnlySet<string> Common { get; } = Set("type", "enum");

    /// <summary>Gets what is valid on a string node.</summary>
    public static IReadOnlySet<string> String { get; } = Set("format", "pattern", "minLength", "maxLength");

    /// <summary>Gets what is valid on an integer or number node.</summary>
    public static IReadOnlySet<string> Numeric { get; } = Set("minimum", "maximum");

    /// <summary>Gets what is valid on an array node.</summary>
    public static IReadOnlySet<string> Array { get; } = Set("items", "minItems", "maxItems");

    /// <summary>Gets what is valid on an object node.</summary>
    public static IReadOnlySet<string> Object { get; } = Set("properties", "required", "additionalProperties");

    private static FrozenSet<string> Set(params string[] keywords) => keywords.ToFrozenSet(StringComparer.Ordinal);
}
