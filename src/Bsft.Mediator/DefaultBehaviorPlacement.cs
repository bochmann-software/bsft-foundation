namespace Bsft.Mediator;

/// <summary>
/// Where a behavior that replaces a default slot runs, passed to
/// <see cref="MediatorConfiguration.ReplaceDefaultBehavior(string, Type, DefaultBehaviorPlacement)"/>.
/// </summary>
/// <remarks>
/// The placement decides the position only. Whichever is chosen, the replacement stays the slot's behavior
/// for every startup check that looks for the slot, the authorization coverage check among them.
/// </remarks>
public enum DefaultBehaviorPlacement
{
    /// <summary>
    /// The replacement runs at the slot's canonical position, where the default behavior would run. This is
    /// what <see cref="MediatorConfiguration.ReplaceDefaultBehavior(string, Type)"/> does.
    /// </summary>
    InSlot = 0,

    /// <summary>
    /// The replacement runs where <see cref="MediatorConfiguration.AddBehavior"/> puts a behavior: after the
    /// default chain, so inside every default behavior, and among the added behaviors in the order of the
    /// configuration calls.
    /// </summary>
    AfterDefaults = 1
}
