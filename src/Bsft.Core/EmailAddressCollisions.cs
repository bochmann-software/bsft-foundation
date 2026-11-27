namespace Bsft.Core;

/// <summary>Finds the stored addresses that the canonical rule would make one.</summary>
/// <remarks>
/// A host that adopts the rule has accounts, invitations and contacts stored under an older one: addresses with an
/// internationalized domain that was refused or kept as typed, different spellings of one domain, an upper-cased domain.
/// Two such rows can be one address under the new rule, and rewriting them blindly would either fail on the unique index
/// halfway through or, worse, merge two accounts. This reports the groups and leaves the decision to the host, for
/// instance in a migration's dry run or an import check.
/// </remarks>
public static class EmailAddressCollisions
{
    /// <summary>Reports what canonicalizing <paramref name="storedAddresses"/> would do.</summary>
    /// <param name="storedAddresses">The addresses as stored; blank entries are ignored and repeats of one spelling count once.</param>
    /// <returns>The report, ordered ordinally so that two runs over the same data read the same.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="storedAddresses"/> is <see langword="null"/>.</exception>
    public static EmailAddressCollisionReport FindCollisions(this IEnumerable<string?> storedAddresses)
    {
        ArgumentNullException.ThrowIfNull(storedAddresses);

        var distinct = storedAddresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var folded = new List<(string Stored, string Key)>();
        var rejected = new List<string>();
        var rewrites = new List<EmailAddressRewrite>();

        foreach (var stored in distinct)
        {
            if (!stored.TryCanonicalize(out var canonical) || !stored.TryFold(out var key))
            {
                rejected.Add(stored);

                continue;
            }

            folded.Add((stored, key!));

            if (!string.Equals(stored, canonical, StringComparison.Ordinal))
                rewrites.Add(new EmailAddressRewrite(stored, canonical!));
        }

        var collisions = folded
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new EmailAddressCollision(group.Key, group.Select(entry => entry.Stored).ToList()))
            .ToList();

        return new EmailAddressCollisionReport(collisions, rejected, rewrites);
    }
}
