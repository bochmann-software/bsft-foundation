namespace Bsft.Core.Persistence;

/// <summary>The parts a time-ordered identifier is made of; see <see cref="TimeOrderedIdGenerator.Decompose"/>.</summary>
/// <param name="CreatedAt">The instant the identifier was made, to the millisecond.</param>
/// <param name="NodeId">The node id of the generator that made it, 0 to <see cref="TimeOrderedIdGenerator.MaxNodeId"/>.</param>
/// <param name="Sequence">The sequence within that millisecond, 0 to <see cref="TimeOrderedIdGenerator.MaxSequence"/>.</param>
public readonly record struct TimeOrderedIdInfo(DateTimeOffset CreatedAt, int NodeId, int Sequence);
