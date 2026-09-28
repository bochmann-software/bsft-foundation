using System.Text.Json.Serialization;

namespace Bsft.Data.Contracts.Filtering;

/// <summary>
/// The base class of the filter AST, carrying the discriminators the four node types are read and
/// written under.
/// </summary>
/// <remarks>
/// Concrete rather than abstract, deliberately: the discriminators are the wire contract, so the set
/// of node types is closed and belongs to this package, and a compiler is expected to reject anything
/// that is none of the four rather than silently treat it as an empty filter. A document that names no
/// discriminator therefore deserializes to this type and is refused there.
/// </remarks>
[JsonDerivedType(typeof(ComparisonNode), "comparison")]
[JsonDerivedType(typeof(PresenceNode), "present")]
[JsonDerivedType(typeof(AndNode), "and")]
[JsonDerivedType(typeof(OrNode), "or")]
public class FilterNode
{
}
