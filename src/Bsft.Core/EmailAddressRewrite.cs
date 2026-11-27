namespace Bsft.Core;

/// <summary>A stored address whose canonical form differs from what is stored.</summary>
/// <param name="Stored">The address as stored.</param>
/// <param name="Canonical">The address as <see cref="EmailAddress.TryCanonicalize"/> returns it.</param>
public sealed record EmailAddressRewrite(string Stored, string Canonical);
