namespace Bsft.Data.Search;

/// <summary>
/// Checks the identifiers a provider package interpolates into full-text DDL.
/// </summary>
/// <remarks>
/// Shared by the three provider packages rather than written out in each: the question of what counts
/// as a safe identifier is the same one whatever the database is, and three copies of the answer is
/// three places for it to drift. Internal because it is not a surface an application reaches; the
/// provider packages see it through <c>InternalsVisibleTo</c>.
/// </remarks>
internal static class SqlIdentifier
{
    /// <summary>
    /// Requires a value to be an identifier that is safe to interpolate.
    /// </summary>
    /// <param name="value">The identifier to check.</param>
    /// <param name="parameterName">The parameter to blame when it is not one.</param>
    /// <returns>The identifier itself, so a caller can assign the checked value.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is not a valid SQL identifier.</exception>
    /// <remarks>
    /// Identifiers are interpolated into DDL, so they are restricted to a safe character set rather
    /// than escaped. A name that needs escaping is a name that should be rejected.
    /// </remarks>
    public static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("An identifier is required.", parameterName);

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
                throw new ArgumentException($"'{value}' is not a valid SQL identifier.", parameterName);
        }

        return value;
    }

    /// <summary>
    /// Requires a column list to name at least one column and every name in it to be an identifier
    /// that is safe to interpolate.
    /// </summary>
    /// <param name="columns">The column names to check.</param>
    /// <param name="parameterName">The parameter to blame when one of them is not one.</param>
    /// <returns>The checked names, in the order they were given.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when no column is given or one of the names is not a valid SQL identifier.
    /// </exception>
    /// <remarks>
    /// An index over no column is a statement no database accepts, so the emptiness check belongs
    /// beside the identifier check rather than beside each provider's own statement builder.
    /// </remarks>
    public static IReadOnlyList<string> RequireColumns(IReadOnlyList<string> columns, string parameterName)
    {
        if (columns is null || columns.Count == 0)
            throw new ArgumentException("At least one column must be indexed.", parameterName);

        return [.. columns.Select(column => Require(column, parameterName))];
    }
}
