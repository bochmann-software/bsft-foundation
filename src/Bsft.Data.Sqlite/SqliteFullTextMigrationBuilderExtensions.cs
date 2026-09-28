using Microsoft.EntityFrameworkCore.Migrations;

namespace Bsft.Data.Sqlite;

/// <summary>
/// Migration operations creating and maintaining a SQLite FTS5 index.
/// </summary>
/// <remarks>
/// <para>
/// Index creation belongs in the migration flow rather than in a manual step that gets forgotten in
/// one environment. The framework still never runs these by itself: they are emitted into the
/// application's own migration, which owns its schema exactly as it does for every other table.
/// </para>
/// <para>
/// Unlike the SQL Server helpers, none of these suppress the migration transaction, and that is a
/// decision rather than an omission. SQLite's DDL is transactional — <c>CREATE VIRTUAL TABLE</c> and
/// <c>CREATE TRIGGER</c> included — so these statements roll back with the rest of the migration when
/// a later operation fails. Suppressing the transaction would throw that away and leave behind the
/// half-created index that the SQL Server provider has to document a manual cleanup for.
/// </para>
/// <para>
/// Every statement is written to be re-runnable (<c>IF NOT EXISTS</c>, <c>IF EXISTS</c>), so a
/// migration re-applied after an unrelated failure does not trip over its own leftovers.
/// </para>
/// </remarks>
public static class SqliteFullTextMigrationBuilderExtensions
{
    /// <summary>
    /// Creates the FTS5 index table if it does not exist yet.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="command">The index to create.</param>
    /// <param name="options">
    /// The application's search options, supplying the tokenizer and its diacritic folding level.
    /// Omitted, the framework's own defaults apply, which is what a deployment that has configured
    /// none already had. Pass the very instance the provider is registered with: it is the only place
    /// the tokenizer is stated, so an index built from a second instance configured alike is an index
    /// nothing keeps in step with what startup validation certifies.
    /// </param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid or no column is given.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the tokenizer is not a declared value.</exception>
    /// <remarks>
    /// <para>
    /// The index is empty afterwards. Creating it over a table that already holds rows is followed by
    /// <see cref="RebuildFullTextIndex"/>; the triggers only carry changes made from then on.
    /// </para>
    /// <para>
    /// The options are handed in rather than resolved from a container because a migration runs
    /// outside one: the application passes the same instance it configures its provider with, so the
    /// folding level is stated once and every migration inherits it. Only this operation takes them —
    /// the triggers and the rebuild carry no tokenizer specification.
    /// </para>
    /// </remarks>
    public static MigrationBuilder CreateFullTextIndex(
        this MigrationBuilder migrationBuilder,
        SqliteCreateFullTextIndexRequest command,
        BsftDataSqliteOptions? options = null)
    {
        Require(migrationBuilder, command);

        migrationBuilder.Sql(FullTextStatements.CreateIndex(command, options ?? new BsftDataSqliteOptions()));
        return migrationBuilder;
    }

    /// <summary>
    /// Creates the triggers that keep the index in step with its content table.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="command">The index the triggers belong to.</param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid or no column is given.</exception>
    /// <remarks>
    /// An external-content index is not maintained by the engine: without these three triggers it
    /// silently answers with whatever was true when it was last rebuilt. They are a separate call so
    /// that a <c>Down</c> migration can drop them, not so that they can be left out.
    /// </remarks>
    public static MigrationBuilder CreateFullTextSyncTriggers(
        this MigrationBuilder migrationBuilder,
        SqliteCreateFullTextIndexRequest command)
    {
        Require(migrationBuilder, command);

        migrationBuilder.Sql(FullTextStatements.CreateInsertTrigger(command));
        migrationBuilder.Sql(FullTextStatements.CreateDeleteTrigger(command));
        migrationBuilder.Sql(FullTextStatements.CreateUpdateTrigger(command));
        return migrationBuilder;
    }

    /// <summary>
    /// Repopulates the index from its content table.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="indexTable">The FTS5 table to rebuild.</param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the table name is invalid.</exception>
    public static MigrationBuilder RebuildFullTextIndex(this MigrationBuilder migrationBuilder, string indexTable)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));

        migrationBuilder.Sql(FullTextStatements.Rebuild(indexTable));
        return migrationBuilder;
    }

    /// <summary>
    /// Drops the synchronisation triggers and the index table.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="indexTable">The FTS5 table to drop.</param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the table name is invalid.</exception>
    /// <remarks>
    /// The triggers go first. A trigger left behind would write into a table that no longer exists,
    /// which fails every insert into the content table — the index would be gone and the application
    /// broken.
    /// </remarks>
    public static MigrationBuilder DropFullTextIndex(this MigrationBuilder migrationBuilder, string indexTable)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));

        foreach (var statement in FullTextStatements.DropTriggers(indexTable))
        {
            migrationBuilder.Sql(statement);
        }

        migrationBuilder.Sql(FullTextStatements.DropIndex(indexTable));
        return migrationBuilder;
    }

    private static void Require(MigrationBuilder migrationBuilder, SqliteCreateFullTextIndexRequest command)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));
        if (command is null)
            throw new ArgumentNullException(nameof(command));
    }
}
