namespace Bsft.Data.Contracts.Search;

/// <summary>
/// A search hit together with its relevance score.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
/// <remarks>
/// The properties are set through object initialization rather than a constructor on purpose:
/// EF Core can order by a member of a member-initialized projection, but not by a member of a
/// constructor-bound one, and ranked results have to be ordered by score inside the database.
/// </remarks>
public sealed record RankedItem<T>
{
    /// <summary>Gets the matched entity.</summary>
    public required T Item { get; init; }

    /// <summary>Gets the relevance score. Higher is more relevant.</summary>
    public required double Score { get; init; }
}
