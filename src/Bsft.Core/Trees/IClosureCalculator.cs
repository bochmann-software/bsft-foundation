namespace Bsft.Core.Trees;

/// <summary>
/// Computes the transitive closure of a tree given as <c>(Id, ParentId)</c> pairs, one row per
/// ancestor and descendant pair, in whatever row type the caller persists.
/// </summary>
/// <remarks>
/// A pure function over the BCL: it knows nothing of the entity it emits, so every package that
/// keeps a closure table (organization units, hierarchy nodes) shares one tested walk and maps to its
/// own entity through the row factory.
/// </remarks>
public interface IClosureCalculator
{
    /// <summary>
    /// Computes all closure rows for the supplied node set. Each node produces a self-row at depth 0
    /// plus one row per ancestor reachable through the parent chain. A node whose <c>ParentId</c> is
    /// not part of the set is treated as a root for the computable part.
    /// </summary>
    /// <typeparam name="TRow">The row type the caller persists or compares.</typeparam>
    /// <param name="nodes">The tree as <c>(Id, ParentId)</c> pairs; a root carries a <see langword="null"/> parent.</param>
    /// <param name="createRow">
    /// Builds one row from <c>(ancestorId, descendantId, depth)</c>, where depth is the number of edges
    /// between the two and zero for the self-row.
    /// </param>
    /// <returns>A read-only collection of closure rows; order is not guaranteed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="nodes"/> or <paramref name="createRow"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a cyclic parent reference is detected in the supplied node set.
    /// </exception>
    IReadOnlyCollection<TRow> Compute<TRow>(
        IEnumerable<(Guid Id, Guid? ParentId)> nodes,
        Func<Guid, Guid, int, TRow> createRow);
}
