namespace Bsft.Core;

/// <summary>What canonicalizing a set of stored addresses would do, reported before anything is changed.</summary>
/// <param name="Collisions">The groups of stored addresses that become one address; none is merged.</param>
/// <param name="Rejected">The stored addresses that are not canonical at all and have to be corrected or retired by hand.</param>
/// <param name="Rewrites">The stored addresses that are canonical but are spelled differently from their canonical form.</param>
public sealed record EmailAddressCollisionReport(
    IReadOnlyList<EmailAddressCollision> Collisions,
    IReadOnlyList<string> Rejected,
    IReadOnlyList<EmailAddressRewrite> Rewrites)
{
    /// <summary>Gets a value indicating whether the addresses can be rewritten without losing a distinction.</summary>
    public bool IsClean => Collisions.Count == 0 && Rejected.Count == 0;
}
