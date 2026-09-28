using System.Text.Json;

namespace Bsft.Data.Contracts.Filtering;

/// <summary>
/// Represents a single field comparison in the filter AST.
/// </summary>
public sealed class ComparisonNode : FilterNode
{
    /// <summary>
    /// Gets the field name to compare against.
    /// </summary>
    /// <remarks>
    /// Read-only for the same reason as <see cref="AndNode.And"/>: the tree a predicate was compiled
    /// from must not change under it, and a leaf is where the field a per-field whitelist was checked
    /// against actually sits. The deserializer fills it through the initializer.
    /// </remarks>
    public required string Field { get; init; }

    /// <summary>
    /// Gets the comparison operator.
    /// </summary>
    /// <remarks>
    /// <see cref="FilterOperator.Present"/> is not one a comparison may carry: presence compares
    /// against nothing and has its own <see cref="PresenceNode"/>. A comparison naming it is a
    /// malformed request, which the compiler in <c>Bsft.Data</c> rejects as such.
    /// </remarks>
    public required FilterOperator Op { get; init; }

    /// <summary>
    /// Gets the value to compare with.
    /// </summary>
    public required JsonElement Value { get; init; }
}
