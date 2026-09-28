namespace Bsft.Mediator;

/// <summary>
/// Orders the default behaviors by the canonical rank of <see cref="DefaultBehaviorCatalog"/> and
/// leaves every other behavior where the host registered it.
/// </summary>
/// <remarks>
/// Ordering happens per dispatch rather than at registration time on purpose. A behavior package
/// registers its own behavior from its own <c>AddBsft*</c> call, which a host may make before or
/// after <c>AddBsftMediator</c>, so registration order is not something the defaults can control —
/// and the failure mode of getting it wrong is caching ahead of authorization.
/// <para>
/// Behaviors the catalog does not know keep their slot in the sequence: only the recognised
/// behaviors are permuted, among the positions they already occupy. A host behavior registered
/// first therefore stays outermost, and one registered last stays innermost.
/// </para>
/// </remarks>
internal sealed class CanonicalPipelineBehaviorOrderer : IPipelineBehaviorOrderer
{
    private readonly IReadOnlyDictionary<Type, int> _ranks;

    /// <summary>
    /// Initializes a new instance of the <see cref="CanonicalPipelineBehaviorOrderer"/> class.
    /// </summary>
    /// <param name="catalog">The catalog supplying the canonical rank per behavior type.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalog"/> is null.</exception>
    public CanonicalPipelineBehaviorOrderer(DefaultBehaviorCatalog catalog)
    {
        if (catalog is null)
            throw new ArgumentNullException(nameof(catalog));

        _ranks = catalog.ResolveRanks();
    }

    /// <inheritdoc />
    public IReadOnlyList<TBehavior> Order<TBehavior>(IReadOnlyList<TBehavior> behaviors)
        where TBehavior : notnull
    {
        if (behaviors is null)
            throw new ArgumentNullException(nameof(behaviors));

        var ranked = CollectRanked(behaviors);

        if (ranked.Count < 2)
            return behaviors;

        // The slots are read off before the sort, because sorting is what moves the behaviors
        // between them. Ordering runs per dispatch, so it stays at one array plus one sort.
        var slots = new int[ranked.Count];
        for (var position = 0; position < ranked.Count; position++)
        {
            slots[position] = ranked[position].Slot;
        }

        ranked.Sort((left, right) => left.Rank.CompareTo(right.Rank));

        var ordered = behaviors.ToArray();
        for (var position = 0; position < slots.Length; position++)
        {
            ordered[slots[position]] = behaviors[ranked[position].Slot];
        }

        return ordered;
    }

    private List<(int Slot, int Rank)> CollectRanked<TBehavior>(IReadOnlyList<TBehavior> behaviors)
        where TBehavior : notnull
    {
        var ranked = new List<(int Slot, int Rank)>();

        for (var slot = 0; slot < behaviors.Count; slot++)
        {
            if (TryGetRank(behaviors[slot], out var rank))
                ranked.Add((slot, rank));
        }

        return ranked;
    }

    private bool TryGetRank<TBehavior>(TBehavior behavior, out int rank)
        where TBehavior : notnull
    {
        var behaviorType = behavior.GetType();

        // The container hands out closed generics (CachingBehavior<ICommand<Unit>, Unit>); the
        // catalog names the open definitions the registrations were made with.
        if (behaviorType.IsGenericType)
            behaviorType = behaviorType.GetGenericTypeDefinition();

        return _ranks.TryGetValue(behaviorType, out rank);
    }
}
