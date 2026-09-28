namespace Bsft.Core.Trees;

/// <summary>
/// Pure, storage-free implementation of <see cref="IClosureCalculator"/> that walks the parent chain
/// of each node and emits one row per ancestor and descendant pair, including the self-row at depth
/// zero.
/// </summary>
public sealed class ClosureCalculator : IClosureCalculator
{
    /// <inheritdoc />
    public IReadOnlyCollection<TRow> Compute<TRow>(
        IEnumerable<(Guid Id, Guid? ParentId)> nodes,
        Func<Guid, Guid, int, TRow> createRow)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(createRow);

        // Single pass: keep the ordered node list for the outer walk and index parents for O(1) look-ups.
        var nodeList = new List<Guid>();
        var parentMap = new Dictionary<Guid, Guid?>();
        foreach (var (id, parentId) in nodes)
        {
            nodeList.Add(id);
            parentMap[id] = parentId;
        }

        // Each node emits at least its self-row; deeper trees emit more, so the list grows past this hint.
        var result = new List<TRow>(nodeList.Count);

        // Reused across nodes to detect cyclic parent references and abort instead of looping forever.
        var visited = new HashSet<Guid>();

        foreach (var id in nodeList)
        {
            // Walk up the ancestor chain starting from the node itself (depth 0).
            visited.Clear();
            var current = id;
            var depth = 0;

            while (true)
            {
                if (!visited.Add(current))
                {
                    throw new InvalidOperationException(
                        $"Cyclic parent reference detected while computing the closure for node '{id}'.");
                }

                result.Add(createRow(current, id, depth));

                // Advance only while the parent exists and is part of the supplied node set. The
                // current node is always in the map: it is either a supplied node or a parent the
                // previous step found there. The parent is read into a plain local rather than a
                // pattern variable so that a mutated condition still compiles: a pattern variable is
                // only definitely assigned under the original condition, and a mutant that cannot
                // compile is never tested.
                var parentId = parentMap[current];
                if (parentId is null || !parentMap.ContainsKey(parentId.Value))
                {
                    break;
                }

                current = parentId.Value;
                depth++;
            }
        }

        return result.AsReadOnly();
    }
}
