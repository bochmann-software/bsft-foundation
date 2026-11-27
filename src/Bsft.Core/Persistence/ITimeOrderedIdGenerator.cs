namespace Bsft.Core.Persistence;

/// <summary>
/// Hands out 64-bit identifiers that the client creates before it writes the row, ordered by the time
/// they were made.
/// </summary>
/// <remarks>
/// <para>
/// A table keyed by <c>(TenantId, Id)</c> has no identity column, so the database cannot number its
/// rows; this seam numbers them instead. Every value one generator returns is greater than the one
/// before it, also when several threads ask at once and when the wall clock steps back, so the order
/// of the ids is the order in which they were made in that process.
/// </para>
/// <para>
/// Two generators produce disjoint values only when their node ids differ. A host that runs several
/// processes against the same database gives each process its own node id; the topic page lists what
/// happens when two share one.
/// </para>
/// </remarks>
public interface ITimeOrderedIdGenerator
{
    /// <summary>Gets the node id this generator stamps into every value it returns.</summary>
    int NodeId { get; }

    /// <summary>Returns the next identifier.</summary>
    /// <returns>A positive value greater than every value this generator returned before.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the clock has passed the last instant the 41-bit time field can hold.
    /// </exception>
    long NextId();
}
