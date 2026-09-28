namespace Bsft.Data.Contracts.Filtering;

/// <summary>
/// Represents a logical AND combination of filter nodes.
/// </summary>
public sealed class AndNode : FilterNode
{
    /// <summary>
    /// Gets the child filter nodes that are combined with AND logic.
    /// </summary>
    /// <remarks>
    /// Read-only because a compiled predicate is only as trustworthy as the tree it was built from:
    /// a caller that could add a child after the compiler walked the node would get a filter that no
    /// longer says what the object says. The deserializer fills it through the initializer.
    /// </remarks>
    public required IReadOnlyList<FilterNode> And { get; init; }
}
