using Bsft.Data.Contracts.Filtering;

namespace Bsft.Data.Tests.Support;

/// <summary>
/// A FilterNode subclass none of the four discriminators names, so the compiler's node switch
/// falls through to the throw that refuses an unknown node type.
/// </summary>
internal sealed class CustomFilterNode : FilterNode
{
}
