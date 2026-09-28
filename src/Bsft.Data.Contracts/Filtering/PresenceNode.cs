namespace Bsft.Data.Contracts.Filtering;

/// <summary>
/// Represents a presence test on a single field — the SCIM (RFC 7644 §3.4.2.2) <c>pr</c> operator.
/// </summary>
/// <remarks>
/// Presence is unary: it asks only whether a field has a value, so — unlike <see cref="ComparisonNode"/>
/// — it carries no <c>value</c>. Keeping it a dedicated node rather than a <see cref="ComparisonNode"/>
/// with an absent value leaves <see cref="ComparisonNode.Value"/> a required member, so the existing
/// comparison contract is unchanged and the wire form stays JSON-honest (no dummy value on a check that
/// has none). It still runs through the same per-field whitelist as every other operator via
/// <see cref="FilterOperator.Present"/>.
/// </remarks>
public sealed class PresenceNode : FilterNode
{
    /// <summary>
    /// Gets the field name to test for presence.
    /// </summary>
    /// <remarks>
    /// Read-only for the same reason as <see cref="AndNode.And"/>: the tree a predicate was compiled
    /// from must not change under it. The deserializer fills it through the initializer.
    /// </remarks>
    public required string Field { get; init; }
}
