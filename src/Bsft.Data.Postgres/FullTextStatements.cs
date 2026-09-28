using Bsft.Data.Search;

namespace Bsft.Data.Postgres;

/// <summary>
/// Produces the DDL for the generated <c>tsvector</c> column and its GIN index.
/// </summary>
/// <remarks>
/// Internal because the statements are not the public surface: applications reach this through the
/// <see cref="PostgresFullTextMigrationBuilderExtensions"/> so that index creation is part of the normal
/// migration flow instead of a manual step that gets forgotten in one environment.
/// </remarks>
internal static class FullTextStatements
{
    /// <summary>PostgreSQL labels lexemes A to D, so at most four columns can be weighted apart.</summary>
    /// <remarks>
    /// The index and the query have to agree on this number or a field would be indexed under a weight
    /// the query never asks for, so <see cref="PostgresSearchProvider"/> reads it from here rather than
    /// stating it a second time.
    /// </remarks>
    internal const int MaxLabeledColumns = 4;

    private static readonly char[] _labels = ['A', 'B', 'C', 'D'];

    /// <summary>
    /// Builds the statement adding the generated <c>tsvector</c> column.
    /// </summary>
    /// <param name="command">The column to add.</param>
    /// <param name="options">
    /// Supplies the default language for a command that names none, and any text search configuration
    /// the deployment registered of its own.
    /// </param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid, no column is given, more
    /// than four columns are given, or the language is unknown.</exception>
    /// <remarks>
    /// A stored generated column rather than a trigger-maintained one: PostgreSQL keeps it in step
    /// itself, so there is no way for the index to drift from the row it describes. The text search
    /// configuration has to be a literal for that — <c>to_tsvector(regconfig, text)</c> is only
    /// immutable in that form, and a generated column accepts nothing else.
    /// </remarks>
    public static string AddVectorColumn(PostgresCreateFullTextIndexRequest command, BsftDataPostgresOptions options)
    {
        var table = SqlIdentifier.Require(command.Table, nameof(command.Table));
        var vector = SqlIdentifier.Require(command.VectorColumn, nameof(command.VectorColumn));
        var columns = RequireColumns(command.Columns);

        var language = command.Language ?? options.DefaultLanguage;

        var configuration = options.ResolveConfiguration(language)
            ?? throw new ArgumentException($"Unknown full-text language '{language}'.", nameof(command.Language));

        var document = string.Join(
            " || ",
            columns.Select((column, index) =>
                $"setweight(to_tsvector('{configuration}', coalesce(\"{column}\", '')), '{_labels[index]}')"));

        return $"ALTER TABLE \"{table}\" ADD COLUMN IF NOT EXISTS \"{vector}\" tsvector "
            + $"GENERATED ALWAYS AS ({document}) STORED;";
    }

    /// <summary>
    /// Builds the statement creating the GIN index over the <c>tsvector</c> column.
    /// </summary>
    /// <param name="command">The index to create.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid.</exception>
    public static string CreateIndex(PostgresCreateFullTextIndexRequest command)
    {
        var table = SqlIdentifier.Require(command.Table, nameof(command.Table));
        var vector = SqlIdentifier.Require(command.VectorColumn, nameof(command.VectorColumn));

        return $"CREATE INDEX IF NOT EXISTS \"{IndexName(table, vector)}\" ON \"{table}\" USING GIN (\"{vector}\");";
    }

    /// <summary>
    /// Builds the statement dropping the GIN index.
    /// </summary>
    /// <param name="table">The indexed table.</param>
    /// <param name="vectorColumn">The indexed column.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid.</exception>
    public static string DropIndex(string table, string vectorColumn) =>
        $"DROP INDEX IF EXISTS \"{IndexName(SqlIdentifier.Require(table, nameof(table)), SqlIdentifier.Require(vectorColumn, nameof(vectorColumn)))}\";";

    /// <summary>
    /// Builds the statement dropping the generated column.
    /// </summary>
    /// <param name="table">The table to alter.</param>
    /// <param name="vectorColumn">The column to drop.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid.</exception>
    public static string DropVectorColumn(string table, string vectorColumn) =>
        $"ALTER TABLE \"{SqlIdentifier.Require(table, nameof(table))}\" "
        + $"DROP COLUMN IF EXISTS \"{SqlIdentifier.Require(vectorColumn, nameof(vectorColumn))}\";";

    private static string IndexName(string table, string vectorColumn) => $"IX_{table}_{vectorColumn}";

    private static IReadOnlyList<string> RequireColumns(IReadOnlyList<string> columns)
    {
        // A fifth column has no label left, and labelling two columns alike would merge weights that
        // the search group declares as different, so the cap is asked before the names are.
        if (columns is { Count: > MaxLabeledColumns })
        {
            throw new ArgumentException(
                $"PostgreSQL weights lexemes with the four labels A to D, so at most {MaxLabeledColumns} columns can be "
                + "indexed with distinct weights.",
                nameof(columns));
        }

        return SqlIdentifier.RequireColumns(columns, nameof(columns));
    }
}
