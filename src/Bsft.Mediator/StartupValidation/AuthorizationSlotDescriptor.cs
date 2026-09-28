namespace Bsft.Mediator.StartupValidation;

/// <summary>
/// One permission system the coverage check knows about: the declarations a request carries, and the
/// default-behavior slot whose behavior enforces them.
/// </summary>
/// <remarks>
/// Both declarations and behavior are named rather than referenced, which is what keeps
/// <c>Bsft.Mediator</c> free of a reference to <c>Bsft.Auth</c> and <c>Bsft.Organization</c>. A
/// third permission system is a fourth entry in the list, not a third branch in the check.
/// </remarks>
/// <param name="BehaviorName">The <see cref="MediatorDefaults"/> slot name of the enforcing behavior.</param>
/// <param name="AttributeTypeNames">The full names of the attributes declaring the permission.</param>
/// <param name="ContractTypeNames">The full names of the interfaces declaring the permission.</param>
internal sealed record AuthorizationSlotDescriptor(
    string BehaviorName,
    IReadOnlyList<string> AttributeTypeNames,
    IReadOnlyList<string> ContractTypeNames);
