namespace Bsft.Data.Contracts.Search;

/// <summary>
/// How a search term is interpreted against the full-text index.
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// Matches terms beginning with the search term. The anchor is on the right (<c>"term*"</c>),
    /// never on the left — a leading wildcard is what makes a search unindexable in the first place.
    /// </summary>
    Prefix = 0,

    /// <summary>
    /// Natural-language matching including stemming and stopword handling. The right mode for free
    /// text such as a candidate profile.
    /// </summary>
    FreeText = 1,
}
