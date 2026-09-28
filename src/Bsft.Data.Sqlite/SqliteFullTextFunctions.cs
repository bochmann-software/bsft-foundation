namespace Bsft.Data.Sqlite;

/// <summary>
/// The two FTS5 functions the provider composes into a query.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's SQLite provider maps neither of them, so this package registers them itself. The methods
/// exist only to be recognised in an expression tree and are never executed: calling one directly
/// throws.
/// </para>
/// <para>
/// The <c>MATCH</c> operator is written in its function form, <c>match(query, column)</c>, which
/// SQLite defines as equivalent to <c>column MATCH query</c>. The function form is what makes the
/// search term a bound parameter rather than text spliced into a statement.
/// </para>
/// </remarks>
internal static class SqliteFullTextFunctions
{
    /// <summary>Matches an FTS5 index against a query string.</summary>
    /// <param name="_query">The FTS5 query expression.</param>
    /// <param name="_matchColumn">The index's hidden column, naming the index to match against.</param>
    /// <returns>Never returns; this method exists only inside an expression tree.</returns>
    /// <exception cref="NotSupportedException">Always thrown when called outside a query.</exception>
    /// <remarks>
    /// The parameters are named with a leading underscore because nothing ever reads them: they exist
    /// to give the call the shape the translation recognises, and the translation works on the
    /// expression tree rather than on any value passed here.
    /// </remarks>
    public static bool IsMatch(string _query, string _matchColumn) => throw NotAQuery();

    /// <summary>Scores a matched row with FTS5's bm25 ranking function.</summary>
    /// <param name="_matchColumn">The index's hidden column, naming the index to score against.</param>
    /// <param name="_columnWeights">
    /// The per-column weights as invariant-culture numbers separated by commas, one per indexed
    /// column. They are a comma-separated string rather than an array because they have to reach the
    /// translation as a constant, and a string is the shape EF Core carries there reliably.
    /// </param>
    /// <returns>Never returns; this method exists only inside an expression tree.</returns>
    /// <exception cref="NotSupportedException">Always thrown when called outside a query.</exception>
    /// <remarks>
    /// bm25 returns a <em>negative</em> number where a better match is more negative, so the provider
    /// negates it to produce a score that sorts descending like every other provider's. The parameters
    /// are unread for the same reason as on <see cref="IsMatch"/>.
    /// </remarks>
    public static double Bm25(string _matchColumn, string _columnWeights) => throw NotAQuery();

    private static NotSupportedException NotAQuery() =>
        new("This method is a marker for query translation and cannot be called directly.");
}
