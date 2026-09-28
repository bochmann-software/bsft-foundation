using Bsft.Data.Search;

namespace Bsft.Data.Sqlite;

/// <summary>
/// Produces the DDL for an FTS5 index and the triggers that keep it in step with its content table.
/// </summary>
/// <remarks>
/// Internal because the statements are not the public surface: applications reach this through the
/// <see cref="SqliteFullTextMigrationBuilderExtensions"/> so that index creation is part of the normal
/// migration flow instead of a manual step that gets forgotten in one environment.
/// </remarks>
internal static class FullTextStatements
{
    /// <summary>
    /// The parameter an invalid column list is blamed on. The names come off the request rather than
    /// off a parameter of their own, so the name a caller sees is stated once here.
    /// </summary>
    private const string ColumnsParameter = "columns";

    /// <summary>The suffixes appended to the index name for the three synchronisation triggers.</summary>
    private static readonly string[] _triggerSuffixes = ["ai", "ad", "au"];

    /// <summary>
    /// Builds the statement creating the FTS5 index table.
    /// </summary>
    /// <param name="command">The index to create.</param>
    /// <param name="options">Supplies the whole tokenizer specification: the tokenizer and how far it folds diacritics.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid or no column is given.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the tokenizer or the folding level is not a declared value.</exception>
    /// <remarks>
    /// <para>
    /// An external-content index (<c>content=</c>) rather than a self-contained one: it stores the
    /// terms but not the text, so the base table stays the single copy of the data. The cost is that
    /// the index does not maintain itself — hence the triggers.
    /// </para>
    /// <para>
    /// Both halves of the tokenizer specification come from the options, and neither from the command.
    /// The options are also what <see cref="SqliteSearchProvider"/> validates a group's language
    /// against at startup, so this is what keeps the index that is created and the index that is
    /// certified from being two different things.
    /// </para>
    /// </remarks>
    public static string CreateIndex(SqliteCreateFullTextIndexRequest command, BsftDataSqliteOptions options)
    {
        var index = SqlIdentifier.Require(command.IndexTable, nameof(command.IndexTable));
        var content = SqlIdentifier.Require(command.ContentTable, nameof(command.ContentTable));
        var key = SqlIdentifier.Require(command.KeyColumn, nameof(command.KeyColumn));
        var columns = SqlIdentifier.RequireColumns(command.Columns, ColumnsParameter);

        var tokenizer = FullTextTokenization.Describe(options.Tokenizer, options.RemoveDiacritics);

        return $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{index}\" USING fts5({Quoted(columns)}, "
            + $"content='{content}', content_rowid='{key}', tokenize='{tokenizer}');";
    }

    /// <summary>
    /// Builds the trigger keeping the index in step with inserts.
    /// </summary>
    /// <param name="command">The index the triggers belong to.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid or no column is given.</exception>
    public static string CreateInsertTrigger(SqliteCreateFullTextIndexRequest command)
    {
        var (index, content, key, columns) = Parts(command);

        return Trigger($"{index}_ai", content, "INSERT", Insert(index, key, columns, "new"));
    }

    /// <summary>
    /// Builds the trigger keeping the index in step with deletes.
    /// </summary>
    /// <param name="command">The index the triggers belong to.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid or no column is given.</exception>
    /// <remarks>
    /// An external-content index is told about a deletion with its <c>'delete'</c> command and the
    /// <em>old</em> values, because it has to remove exactly the terms it once added. Deleting the
    /// base row first and reading the columns afterwards would find nothing, which is why this runs
    /// as an <c>AFTER DELETE</c> trigger over <c>old</c>.
    /// </remarks>
    public static string CreateDeleteTrigger(SqliteCreateFullTextIndexRequest command)
    {
        var (index, content, key, columns) = Parts(command);

        return Trigger($"{index}_ad", content, "DELETE", Delete(index, key, columns));
    }

    /// <summary>
    /// Builds the trigger keeping the index in step with updates.
    /// </summary>
    /// <param name="command">The index the triggers belong to.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid or no column is given.</exception>
    /// <remarks>
    /// An update is a delete of the old terms followed by an insert of the new ones. FTS5 has no
    /// update command, and skipping the delete would leave the old terms behind — a row that stays
    /// findable under a name it no longer has.
    /// </remarks>
    public static string CreateUpdateTrigger(SqliteCreateFullTextIndexRequest command)
    {
        var (index, content, key, columns) = Parts(command);

        var body = $"{Delete(index, key, columns)} {Insert(index, key, columns, "new")}";

        return Trigger($"{index}_au", content, "UPDATE", body);
    }

    /// <summary>
    /// Builds the statement repopulating the index from its content table.
    /// </summary>
    /// <param name="indexTable">The FTS5 table to rebuild.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when the table name is invalid.</exception>
    public static string Rebuild(string indexTable)
    {
        var index = SqlIdentifier.Require(indexTable, nameof(indexTable));

        return $"INSERT INTO \"{index}\"(\"{index}\") VALUES ('rebuild');";
    }

    /// <summary>
    /// Builds the statements dropping the synchronisation triggers.
    /// </summary>
    /// <param name="indexTable">The FTS5 table whose triggers are dropped.</param>
    /// <returns>One statement per trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when the table name is invalid.</exception>
    public static IReadOnlyList<string> DropTriggers(string indexTable)
    {
        var index = SqlIdentifier.Require(indexTable, nameof(indexTable));

        return [.. _triggerSuffixes.Select(suffix => $"DROP TRIGGER IF EXISTS \"{index}_{suffix}\";")];
    }

    /// <summary>
    /// Builds the statement dropping the index table.
    /// </summary>
    /// <param name="indexTable">The FTS5 table to drop.</param>
    /// <returns>The SQL statement.</returns>
    /// <exception cref="ArgumentException">Thrown when the table name is invalid.</exception>
    public static string DropIndex(string indexTable) =>
        $"DROP TABLE IF EXISTS \"{SqlIdentifier.Require(indexTable, nameof(indexTable))}\";";

    /// <summary>Wraps a trigger body in the statement that declares it.</summary>
    private static string Trigger(string name, string contentTable, string operation, string body) =>
        $"CREATE TRIGGER IF NOT EXISTS \"{name}\" AFTER {operation} ON \"{contentTable}\" BEGIN {body} END;";

    private static (string Index, string Content, string Key, IReadOnlyList<string> Columns) Parts(
        SqliteCreateFullTextIndexRequest command) =>
        (SqlIdentifier.Require(command.IndexTable, nameof(command.IndexTable)),
            SqlIdentifier.Require(command.ContentTable, nameof(command.ContentTable)),
            SqlIdentifier.Require(command.KeyColumn, nameof(command.KeyColumn)),
            SqlIdentifier.RequireColumns(command.Columns, ColumnsParameter));

    private static string Insert(string index, string key, IReadOnlyList<string> columns, string row) =>
        $"INSERT INTO \"{index}\"(\"rowid\", {Quoted(columns)}) "
        + $"VALUES ({row}.\"{key}\", {string.Join(", ", columns.Select(c => $"{row}.\"{c}\""))});";

    private static string Delete(string index, string key, IReadOnlyList<string> columns) =>
        $"INSERT INTO \"{index}\"(\"{index}\", \"rowid\", {Quoted(columns)}) "
        + $"VALUES ('delete', old.\"{key}\", {string.Join(", ", columns.Select(c => $"old.\"{c}\""))});";

    private static string Quoted(IReadOnlyList<string> columns) =>
        string.Join(", ", columns.Select(c => $"\"{c}\""));

}
