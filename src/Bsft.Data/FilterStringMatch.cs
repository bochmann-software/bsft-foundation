namespace Bsft.Data;

/// <summary>
/// Selects how the filter compiler makes string comparisons case-insensitive, which decides whether a
/// provider can serve them from an index.
/// </summary>
/// <remarks>
/// The choice is a deliberate trade-off a deployment states, because only the deployment knows its
/// column collation. <see cref="LowerInvariant"/> is portable and correct on any collation but wraps
/// the column in <c>LOWER(COALESCE(col, ''))</c>, which no ordinary index can serve.
/// <see cref="Collation"/> leaves the column bare so a case-insensitive column collation carries the
/// case-folding and the index stays usable, but it is only case-insensitive when the column actually
/// has such a collation.
/// </remarks>
public enum FilterStringMatch
{
    /// <summary>
    /// Folds both sides to lower case with <c>string.ToLower</c>. Case-insensitive on every collation
    /// and provider, but the resulting <c>LOWER(COALESCE(col, ''))</c> on the column is non-sargable,
    /// so the query cannot use an index on that column. This is the default, matching historical
    /// behaviour.
    /// </summary>
    LowerInvariant = 0,

    /// <summary>
    /// Compares the bare column against the value and lets the column's own collation decide case
    /// sensitivity. Sargable — an index on the column stays usable — but only case-insensitive when the
    /// column is declared with a case-insensitive collation. Opt in only when that is guaranteed.
    /// </summary>
    Collation = 1
}
