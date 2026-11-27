namespace Bsft.Core;

/// <summary>Stored addresses that are one address once canonicalized.</summary>
/// <param name="Key">The shared key (see <see cref="EmailAddress.TryFold"/>).</param>
/// <param name="Addresses">The stored spellings, distinct and in ordinal order; always at least two.</param>
public sealed record EmailAddressCollision(string Key, IReadOnlyList<string> Addresses);
