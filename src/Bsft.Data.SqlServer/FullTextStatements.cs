using System.Globalization;
using Bsft.Data.Search;

namespace Bsft.Data.SqlServer;

/// <summary>
/// Produces the DDL for a full-text catalogue, index and stopword list.
/// </summary>
/// <remarks>
/// Internal because the statements are not the public surface: applications reach this through the
/// <see cref="SqlServerFullTextMigrationBuilderExtensions"/> so that index creation is part of the normal
/// migration flow instead of a manual DBA step that gets forgotten in one environment.
/// </remarks>
internal static class FullTextStatements
{
    /// <summary>
    /// Builds the statement creating a full-text catalogue if it does not exist yet.
    /// </summary>
    /// <param name="catalogName">The catalogue name.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is null, whitespace or not a valid identifier.</exception>
    public static string CreateCatalog(string catalogName)
    {
        var name = SqlIdentifier.Require(catalogName, nameof(catalogName));

        return $"IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{name}') "
            + $"CREATE FULLTEXT CATALOG [{name}];";
    }

    /// <summary>
    /// Builds the statement creating a full-text index over the given columns.
    /// </summary>
    /// <param name="table">The table to index.</param>
    /// <param name="keyIndexName">The name of the table's unique key index, required by SQL Server.</param>
    /// <param name="catalogName">The catalogue to create the index in.</param>
    /// <param name="columns">The columns to index.</param>
    /// <param name="language">A two-letter language code such as <c>de</c> driving stemming and stopwords.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when an identifier is invalid, no column is given, or the language does not map to a
    /// known locale.
    /// </exception>
    public static string CreateIndex(
        string table,
        string keyIndexName,
        string catalogName,
        IReadOnlyList<string> columns,
        string language)
    {
        var tableName = SqlIdentifier.Require(table, nameof(table));
        var keyIndex = SqlIdentifier.Require(keyIndexName, nameof(keyIndexName));
        var catalog = SqlIdentifier.Require(catalogName, nameof(catalogName));

        var indexedColumns = SqlIdentifier.RequireColumns(columns, nameof(columns));

        var lcid = FullTextLanguageResolver.Resolve(language)
            ?? throw new ArgumentException($"Unknown full-text language '{language}'.", nameof(language));

        var columnList = string.Join(
            ", ",
            indexedColumns.Select(c => $"[{c}] LANGUAGE {lcid.ToString(CultureInfo.InvariantCulture)}"));

        return $"CREATE FULLTEXT INDEX ON [{tableName}] ({columnList}) "
            + $"KEY INDEX [{keyIndex}] ON [{catalog}] WITH CHANGE_TRACKING AUTO;";
    }

    /// <summary>
    /// Builds the statement attaching a stopword list to a full-text index.
    /// </summary>
    /// <param name="table">The table whose index is altered.</param>
    /// <param name="stopList">The stoplist name, or the <c>SYSTEM</c> and <c>OFF</c> keywords.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid.</exception>
    public static string SetStopList(string table, string stopList)
    {
        var tableName = SqlIdentifier.Require(table, nameof(table));

        if (string.IsNullOrWhiteSpace(stopList))
            throw new ArgumentException("A stoplist is required.", nameof(stopList));

        // SYSTEM and OFF are keywords rather than object names, so bracketing them would turn each
        // into the name of a stoplist that does not exist.
        var target = IsStopListKeyword(stopList)
            ? stopList.ToUpperInvariant()
            : $"[{SqlIdentifier.Require(stopList, nameof(stopList))}]";

        return $"ALTER FULLTEXT INDEX ON [{tableName}] SET STOPLIST {target};";
    }

    /// <summary>
    /// Builds the statement dropping a full-text index.
    /// </summary>
    /// <param name="table">The table whose index is dropped.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when the table name is invalid.</exception>
    public static string DropIndex(string table) =>
        $"DROP FULLTEXT INDEX ON [{SqlIdentifier.Require(table, nameof(table))}];";

    private static bool IsStopListKeyword(string value) =>
        string.Equals(value, "SYSTEM", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "OFF", StringComparison.OrdinalIgnoreCase);
}
