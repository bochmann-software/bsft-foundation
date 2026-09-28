using Bsft.Core;

namespace Bsft.Mediator.StartupValidation;

/// <summary>
/// Startup check that reports a request type declaring a permission which no registered pipeline
/// behavior enforces. Such a declaration is decorative: the request reaches its handler unchecked,
/// and nothing else in the wiring says so.
/// </summary>
/// <remarks>
/// It runs regardless of <c>UseDefaults()</c>, because the gap is not a property of the default
/// chain: a host that wires its own pipeline can leave the same hole, and a host that calls
/// <c>UseDefaults()</c> without referencing <c>Bsft.Auth.Mediator</c> gets a slot that resolves to
/// nothing and therefore no complaint from the default-behavior check.
/// <para>
/// The finding is reported as a warning on a successful <see cref="StartupValidationResult"/> rather
/// than as an error. Registration stays opt-in — a permission enforced by an endpoint policy alone
/// is a legitimate deployment — so the framework says what is unenforced and leaves the decision
/// with the host.
/// </para>
/// <para>
/// What the check can and cannot see is worth stating plainly. It verifies that the enforcing
/// behavior is registered, by implementation type: an open-generic and a closed-generic registration
/// are both recognised, but a behavior wired through an <c>ImplementationFactory</c> delegate is
/// invisible to it and produces a warning that names no real gap. It says nothing about pipeline
/// order either, so a hand-wired chain in which a short-circuiting behavior runs ahead of
/// authorization passes this check while enforcement never runs. Ordering is the host's
/// responsibility; <c>UseDefaults()</c> is what guarantees it.
/// </para>
/// <para>
/// The enforcing behaviors are matched by name through <see cref="DefaultBehaviorCatalog"/>, exactly
/// as the default chain resolves them, so <c>Bsft.Mediator</c> keeps its dependency closure and does
/// not reference <c>Bsft.Auth</c> or <c>Bsft.Organization</c> for this check. The declarations are
/// recognised the same way, through <see cref="AuthorizationSlotDescriptor"/>.
/// </para>
/// </remarks>
public sealed class AuthorizationCoverageValidator : IStartupValidator
{
    private const string TenantAuthorizeAttributeName = "Bsft.Auth.Authorization.TenantAuthorizeAttribute";
    private const string OrganizationPermissionContractName = "Bsft.Organization.Mediator.IRequireOrganizationPermission";

    private readonly MediatorConfiguration _config;
    private readonly MediatorTypeScanner _scanner;
    private readonly PipelineBehaviorRegistrations? _registrations;

    /// <summary>
    /// The permission systems the check knows about. Adding one is an entry here, not a branch.
    /// </summary>
    private readonly IReadOnlyList<AuthorizationSlotDescriptor> _slots =
    [
        new(
            MediatorDefaults.Authorization,
            [typeof(RequirePermissionAttribute).FullName!, TenantAuthorizeAttributeName],
            []),
        new(
            MediatorDefaults.OrganizationAuthorization,
            [],
            [OrganizationPermissionContractName])
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationCoverageValidator"/> class.
    /// </summary>
    /// <param name="configuration">The mediator configuration holding the catalog.</param>
    /// <param name="scanner">The shared reflection pass over the configured assemblies.</param>
    /// <param name="registrations">
    /// The behavior registrations of the container, or <see langword="null"/> when they are not
    /// available — the check then cannot tell a wired behavior from a missing one and says nothing.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> or <paramref name="scanner"/> is null.</exception>
    internal AuthorizationCoverageValidator(
        MediatorConfiguration configuration,
        MediatorTypeScanner scanner,
        PipelineBehaviorRegistrations? registrations)
    {
        _config = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _registrations = registrations;
    }

    /// <summary>
    /// Scans the configured assemblies for requests declaring a permission and reports a warning for
    /// every declaration the pipeline does not enforce. The result is always successful: an
    /// unenforced declaration is reported, never fatal.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A successful <see cref="StartupValidationResult"/> summarising what was checked.</returns>
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var declaringTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slot in _slots)
        {
            var declarations = CollectDeclarations(slot, IsRequest);
            declaringTypes.UnionWith(declarations);

            if (DescribeUnenforced(slot, declarations) is { } warning)
                warnings.Add(warning);

            var notifications = CollectDeclarations(slot, IsNotification);
            declaringTypes.UnionWith(notifications);

            if (DescribeUnenforceable(notifications) is { } notificationWarning)
                warnings.Add(notificationWarning);
        }

        var message =
            $"Checked {declaringTypes.Count} request type(s) declaring a permission against the registered pipeline behaviors.";

        return Task.FromResult(StartupValidationResult.Success(message, warnings));
    }

    /// <summary>
    /// The names of the dispatched types carrying one of a slot's declarations. Types are named in
    /// full: two requests of the same simple name in different namespaces are two gaps, and a warning
    /// naming only the simple name would not say which of them to fix.
    /// </summary>
    /// <param name="slot">The permission system to look for.</param>
    /// <param name="isDispatched">Reports whether a type is dispatched in the shape being collected.</param>
    /// <returns>The full names of the declaring types.</returns>
    private IReadOnlyList<string> CollectDeclarations(
        AuthorizationSlotDescriptor slot, Func<IReadOnlyList<Type>, bool> isDispatched)
    {
        var declarations = new List<string>();

        foreach (var scanned in _scanner.ScanTypes())
        {
            // Interfaces are abstract too, so the one test covers both: a contract declaring a
            // permission is enforced on the concrete request that implements it, not on itself.
            if (scanned.Type.IsAbstract || !isDispatched(scanned.Interfaces))
                continue;

            if (HasDeclaration(slot, scanned))
                declarations.Add(scanned.Type.FullName ?? scanned.Type.Name);
        }

        return declarations;
    }

    /// <summary>
    /// The warning for notification types declaring a permission, or <see langword="null"/> when
    /// there are none.
    /// </summary>
    /// <param name="notificationTypeNames">The declaring notification types.</param>
    /// <returns>The warning, or <see langword="null"/>.</returns>
    /// <remarks>
    /// A command or query declaring a permission no behavior enforces can be closed by registering
    /// the behavior. A notification cannot: publishing bypasses the pipeline altogether, so no
    /// wiring makes the declaration mean anything, and the gap would otherwise be the only one this
    /// check stays silent about.
    /// </remarks>
    private static string? DescribeUnenforceable(IReadOnlyList<string> notificationTypeNames)
    {
        if (notificationTypeNames.Count == 0)
            return null;

        return
            $"""
             {notificationTypeNames.Count} notification type(s) declare a permission that the
             mediator cannot enforce: {string.Join(", ", notificationTypeNames)}.
             Notifications are published straight to their handlers and never enter the behavior
             pipeline, so no registration closes this gap. Enforce the permission where the
             notification is raised, or in the handler, and remove the declaration.
             """;
    }

    /// <summary>
    /// The warning for a slot whose behavior is not registered — because its package is absent,
    /// because its registration call was never made, or because the slot was suppressed — or
    /// <see langword="null"/> when there is nothing to report.
    /// </summary>
    private string? DescribeUnenforced(AuthorizationSlotDescriptor slot, IReadOnlyList<string> requestTypeNames)
    {
        if (requestTypeNames.Count == 0)
            return null;

        // A catalog without the slot is one the tests supply; there is no call to name, so there is
        // nothing actionable to say. The same holds for a container without the registrations, where
        // the check cannot tell a registered behavior from a missing one at all.
        var descriptor = _config.Catalog.FindSlot(slot.BehaviorName);
        if (descriptor is null || _registrations is null)
            return null;

        var behaviorType = _config.Catalog.ResolveBehaviorTypeName(descriptor.TypeName);
        if (behaviorType is not null && _registrations.IsRegistered(behaviorType))
            return null;

        return
            $"""
             {requestTypeNames.Count} dispatched request type(s) declare a permission that no
             registered pipeline behavior enforces: {string.Join(", ", requestTypeNames)}.
             The declaration is decorative until '{descriptor.TypeName}' runs in the mediator
             pipeline: those requests reach their handlers unchecked.
             Call {descriptor.RegistrationCall} (next to UseDefaults(), or AddBehavior for a
             hand-wired chain), or remove the declaration from those requests if another gate
             already enforces it.
             The check reads registrations only: a behavior wired through a factory delegate cannot
             be seen and is reported here even though it runs, and pipeline order is not verified at
             all — in a hand-wired chain a short-circuiting behavior ahead of authorization keeps
             this check quiet while enforcement never runs. UseDefaults() is what guarantees the
             order.
             """;
    }

    /// <summary>
    /// Reports whether a type is published as a notification, which never enters the pipeline.
    /// </summary>
    private static bool IsNotification(IReadOnlyList<Type> interfaces) =>
        interfaces.Contains(typeof(INotification));

    /// <summary>
    /// Reports whether a type is dispatched as a command or a query, the only shapes a pipeline
    /// behavior ever sees.
    /// </summary>
    private static bool IsRequest(IReadOnlyList<Type> interfaces) =>
        interfaces.Any(iface =>
            iface.IsGenericType &&
            (iface.GetGenericTypeDefinition() == typeof(ICommand<>) ||
             iface.GetGenericTypeDefinition() == typeof(IQuery<>)));

    /// <summary>
    /// Reports whether a request carries one of a slot's declarations. Both attributes and contracts
    /// are matched by full name, because they live in packages this one does not reference.
    /// </summary>
    private static bool HasDeclaration(AuthorizationSlotDescriptor slot, ScannedType scanned) =>
        scanned.Type.GetCustomAttributes(inherit: true).Any(attribute =>
            slot.AttributeTypeNames.Contains(attribute.GetType().FullName ?? string.Empty, StringComparer.Ordinal)) ||
        scanned.Interfaces.Any(iface =>
            slot.ContractTypeNames.Contains(iface.FullName ?? string.Empty, StringComparer.Ordinal));
}
