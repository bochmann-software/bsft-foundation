namespace Bsft.Mediator;

/// <summary>
/// One slot of the canonical default pipeline: the name a host suppresses the behavior by, the
/// assembly-qualified behavior type filling the slot, and the registration call that supplies the
/// behavior's own dependencies.
/// </summary>
/// <remarks>
/// The type is named rather than referenced, which is what keeps <c>Bsft.Mediator</c> free of a
/// reference to every behavior package while still being able to order them. Resolving the name is
/// <see cref="DefaultBehaviorCatalog"/>'s job.
/// </remarks>
/// <param name="Name">The name used by <see cref="MediatorConfiguration.SuppressDefaultBehavior"/>.</param>
/// <param name="TypeName">The assembly-qualified name of the open-generic behavior type.</param>
/// <param name="RegistrationCall">The call registering the services the behavior needs.</param>
/// <param name="Placement">
/// Where the behavior runs: at the slot's canonical position, or after the default chain for a replacement
/// placed there by <see cref="MediatorConfiguration.ReplaceDefaultBehavior(string, Type, DefaultBehaviorPlacement)"/>.
/// </param>
internal sealed record DefaultBehaviorDescriptor(
    string Name,
    string TypeName,
    string RegistrationCall,
    DefaultBehaviorPlacement Placement = DefaultBehaviorPlacement.InSlot);
