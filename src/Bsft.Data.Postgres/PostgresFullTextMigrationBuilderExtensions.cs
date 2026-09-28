using Microsoft.EntityFrameworkCore.Migrations;

namespace Bsft.Data.Postgres;

/// <summary>
/// Migration operations creating and maintaining the PostgreSQL full-text column and index.
/// </summary>
/// <remarks>
/// <para>
/// Index creation belongs in the migration flow rather than in a manual step that gets forgotten in
/// one environment. The framework still never runs these by itself: they are emitted into the
/// application's own migration, which owns its schema exactly as it does for every other table.
/// </para>
/// <para>
/// None of these suppress the migration transaction, and that is a decision rather than an omission.
/// PostgreSQL's DDL is transactional, <c>ALTER TABLE</c> and a plain <c>CREATE INDEX</c> included, so
/// these statements roll back with the rest of a failing migration instead of leaving a half-built
/// index behind the way the SQL Server helpers have to.
/// </para>
/// <para>
/// <c>CREATE INDEX CONCURRENTLY</c> is deliberately not offered. It is the one form that would need
/// the transaction suppressed, and it would buy nothing here: adding a stored generated column
/// rewrites the table under an <c>ACCESS EXCLUSIVE</c> lock in the same migration, so the index is
/// not what makes this a maintenance-window operation. On a large table, add the column in its own
/// migration and plan for the rewrite.
/// </para>
/// </remarks>
public static class PostgresFullTextMigrationBuilderExtensions
{
    /// <summary>
    /// Adds the generated <c>tsvector</c> column if it does not exist yet.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="command">The column to add.</param>
    /// <param name="options">
    /// The application's search options, supplying the default stemming language and any text search
    /// configuration it registered. Omitted, the framework's own defaults apply, which is what a
    /// deployment that has configured neither already had.
    /// </param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid, no column is given, more
    /// than four columns are given, or the language is unknown.</exception>
    /// <remarks>
    /// <para>
    /// The column is generated and stored, so PostgreSQL keeps it in step with the row itself.
    /// Nothing has to be rebuilt after a bulk import and no trigger can be forgotten.
    /// </para>
    /// <para>
    /// The options are handed in rather than resolved from a container because a migration runs
    /// outside one: the application passes the same instance it configures its provider with, so the
    /// stemming language is stated once and every migration inherits it.
    /// </para>
    /// </remarks>
    public static MigrationBuilder AddFullTextVector(
        this MigrationBuilder migrationBuilder,
        PostgresCreateFullTextIndexRequest command,
        BsftDataPostgresOptions? options = null)
    {
        Require(migrationBuilder, command);

        migrationBuilder.Sql(FullTextStatements.AddVectorColumn(command, options ?? new BsftDataPostgresOptions()));
        return migrationBuilder;
    }

    /// <summary>
    /// Creates the GIN index over the <c>tsvector</c> column.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="command">The index to create.</param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid.</exception>
    /// <remarks>
    /// GIN rather than GiST: a GIN index is the slower one to build and the faster one to search,
    /// which is the trade every search endpoint wants.
    /// </remarks>
    public static MigrationBuilder CreateFullTextIndex(
        this MigrationBuilder migrationBuilder,
        PostgresCreateFullTextIndexRequest command)
    {
        Require(migrationBuilder, command);

        migrationBuilder.Sql(FullTextStatements.CreateIndex(command));
        return migrationBuilder;
    }

    /// <summary>
    /// Drops the GIN index and the generated column.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="table">The indexed table.</param>
    /// <param name="vectorColumn">The generated column to drop.</param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid.</exception>
    public static MigrationBuilder DropFullTextIndex(
        this MigrationBuilder migrationBuilder,
        string table,
        string vectorColumn = BsftDataPostgresOptions.DefaultVectorProperty)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));

        // The index goes first: dropping the column would drop the index with it, and an explicit
        // order keeps the Down migration readable as the mirror of the Up.
        migrationBuilder.Sql(FullTextStatements.DropIndex(table, vectorColumn));
        migrationBuilder.Sql(FullTextStatements.DropVectorColumn(table, vectorColumn));
        return migrationBuilder;
    }

    private static void Require(MigrationBuilder migrationBuilder, PostgresCreateFullTextIndexRequest command)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));
        if (command is null)
            throw new ArgumentNullException(nameof(command));
    }
}
