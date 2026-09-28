namespace Bsft.Mediator;

/// <summary>
/// The canonical order of the default pipeline behaviors, outermost first. The framework states the
/// order here instead of leaving it to each host, because getting it wrong is a security bug rather
/// than a cosmetic one — caching ahead of authorization serves a cached result to a caller who was
/// never allowed to see it.
/// </summary>
/// <remarks>
/// Positions 2 (correlation) and 5 (idempotency) of the documented order are reserved: no pipeline
/// behavior ships for either concern yet, so the catalog holds no slot that could never be filled.
/// The behaviors of other packages are named rather than referenced, so <c>Bsft.Mediator</c> keeps
/// its dependency closure and a package the host does not reference simply resolves to nothing.
/// </remarks>
internal sealed class DefaultBehaviorCatalog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultBehaviorCatalog"/> class holding the
    /// canonical slots.
    /// </summary>
    public DefaultBehaviorCatalog()
        : this(
        [
            new(
                MediatorDefaults.Tracing,
                "Bsft.Mediator.TracingBehavior`2, Bsft.Mediator",
                "AddBsftMediator(...)"),
            new(
                MediatorDefaults.Authorization,
                "Bsft.Auth.Mediator.AuthorizationBehavior`2, Bsft.Auth.Mediator",
                "AddBsftAuthMediator()"),
            new(
                MediatorDefaults.OrganizationAuthorization,
                "Bsft.Organization.Mediator.OrganizationAuthorizationBehavior`2, Bsft.Organization.Mediator",
                "AddBsftOrganizationMediator()"),
            new(
                MediatorDefaults.Validation,
                "Bsft.Mediator.ValidationBehavior`2, Bsft.Mediator",
                "AddBsftMediator(...)"),
            new(
                MediatorDefaults.Audit,
                "Bsft.Auditing.Mediator.AuditBehavior`2, Bsft.Auditing.Mediator",
                "AddBsftAuditMediator()"),
            new(
                MediatorDefaults.Retry,
                "Bsft.Resilience.Mediator.RetryBehavior`2, Bsft.Resilience.Mediator",
                "AddBsftResilienceMediator(...)"),
            new(
                MediatorDefaults.Transaction,
                "Bsft.Mediator.Transactions.TransactionBehavior`2, Bsft.Mediator",
                "AddBsftMediator(...)"),
            new(
                MediatorDefaults.Caching,
                "Bsft.Caching.Mediator.CachingBehavior`2, Bsft.Caching.Mediator",
                "AddBsftCachingMediator()")
        ])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultBehaviorCatalog"/> class over the given
    /// slots. Used by the tests that exercise a slot whose package is absent.
    /// </summary>
    /// <param name="descriptors">The slots, outermost first.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptors"/> is null.</exception>
    internal DefaultBehaviorCatalog(IReadOnlyList<DefaultBehaviorDescriptor> descriptors)
    {
        Descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
    }

    /// <summary>
    /// The canonical slots, outermost first.
    /// </summary>
    public IReadOnlyList<DefaultBehaviorDescriptor> Descriptors { get; }

    /// <summary>
    /// Reports whether a name matches a slot, so a suppression that matches nothing can be rejected
    /// rather than silently leaving a security behavior in place the host believed it had removed.
    /// </summary>
    /// <param name="name">The behavior name to look up.</param>
    /// <returns><see langword="true"/> when the catalog holds a slot of that name.</returns>
    public bool IsKnown(string name) =>
        Descriptors.Any(descriptor => IsSameName(descriptor.Name, name));

    /// <summary>
    /// The slot of a given name, so a check about one behavior can name the call that registers it
    /// without walking the whole chain.
    /// </summary>
    /// <param name="name">The behavior name to look up.</param>
    /// <returns>The slot, or <see langword="null"/> when the catalog holds none of that name.</returns>
    public DefaultBehaviorDescriptor? FindSlot(string name) =>
        Descriptors.FirstOrDefault(descriptor => IsSameName(descriptor.Name, name));

    /// <summary>
    /// Resolves the assembly-qualified type name of a slot, so a caller that already holds the slot
    /// gets its behavior type without searching the catalog for the same name twice.
    /// </summary>
    /// <param name="typeName">The assembly-qualified name taken from a slot.</param>
    /// <returns>
    /// The open-generic behavior type, or <see langword="null"/> when the package shipping it is not
    /// referenced by the application.
    /// </returns>
    public Type? ResolveBehaviorTypeName(string typeName) =>
        Type.GetType(typeName, throwOnError: false);

    /// <summary>
    /// The slot names, for the diagnostic of a suppression that matched nothing.
    /// </summary>
    /// <returns>The known names, comma separated, in canonical order.</returns>
    public string DescribeKnownNames() =>
        string.Join(", ", Descriptors.Select(descriptor => descriptor.Name));

    /// <summary>
    /// The slots the host has not suppressed and whose package is referenced, paired with the
    /// behavior type filling them, in canonical order. A slot whose package is absent is left out
    /// here, which is the whole of "an unreferenced package contributes nothing".
    /// </summary>
    /// <param name="suppressedNames">The names the host suppressed.</param>
    /// <returns>The available slots and their behavior types, outermost first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="suppressedNames"/> is null.</exception>
    public IReadOnlyList<KeyValuePair<DefaultBehaviorDescriptor, Type>> Available(
        IReadOnlyCollection<string> suppressedNames)
    {
        if (suppressedNames is null)
            throw new ArgumentNullException(nameof(suppressedNames));

        var available = new List<KeyValuePair<DefaultBehaviorDescriptor, Type>>();

        foreach (var descriptor in Descriptors)
        {
            if (suppressedNames.Any(suppressed => IsSameName(descriptor.Name, suppressed)))
                continue;

            if (ResolveType(descriptor) is { } behaviorType)
                available.Add(new KeyValuePair<DefaultBehaviorDescriptor, Type>(descriptor, behaviorType));
        }

        return available;
    }

    /// <summary>
    /// The rank of every available default behavior type, keyed by its open-generic type definition
    /// and counted from the outermost slot. Suppression is irrelevant here: a behavior the host
    /// registers itself still belongs in its canonical position.
    /// </summary>
    /// <returns>The rank per behavior type definition.</returns>
    public IReadOnlyDictionary<Type, int> ResolveRanks()
    {
        var available = Available([]);
        var ranks = new Dictionary<Type, int>();

        for (var rank = 0; rank < available.Count; rank++)
        {
            ranks[available[rank].Value] = rank;
        }

        return ranks;
    }

    /// <summary>
    /// Resolves the behavior type of a slot, or <see langword="null"/> when the package shipping it
    /// is not referenced by the application.
    /// </summary>
    private static Type? ResolveType(DefaultBehaviorDescriptor descriptor) =>
        Type.GetType(descriptor.TypeName, throwOnError: false);

    private static bool IsSameName(string descriptorName, string candidate) =>
        string.Equals(descriptorName, candidate, StringComparison.OrdinalIgnoreCase);
}
