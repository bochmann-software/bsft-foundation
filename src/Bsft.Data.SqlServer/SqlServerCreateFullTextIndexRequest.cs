namespace Bsft.Data.SqlServer;

/// <summary>
/// Describes the full-text index a migration creates.
/// </summary>
/// <param name="Table">The table to index.</param>
/// <param name="KeyIndexName">The name of the table's unique key index, required by SQL Server.</param>
/// <param name="CatalogName">The catalogue to create the index in.</param>
/// <param name="Columns">The columns to index.</param>
/// <param name="Language">A two-letter language code such as <c>de</c> driving stemming and stopwords.</param>
public sealed record SqlServerCreateFullTextIndexRequest(
    string Table,
    string KeyIndexName,
    string CatalogName,
    IReadOnlyList<string> Columns,
    string Language = "en");
