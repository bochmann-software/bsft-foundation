namespace Bsft.Data.Contracts.Filtering;

/// <summary>
/// Represents a logical OR combination of filter nodes.
/// </summary>
public sealed class OrNode : FilterNode
{
    /// <summary>
    /// Gets the child filter nodes that are combined with OR logic.
    /// </summary>
    /// <remarks>
    /// Read-only for the same reason as <see cref="AndNode.And"/>: the tree a predicate was compiled
    /// from must not change under it. The deserializer fills it through the initializer.
    /// </remarks>
    public required IReadOnlyList<FilterNode> Or { get; init; }
}
