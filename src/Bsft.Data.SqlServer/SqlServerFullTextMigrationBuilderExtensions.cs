using Microsoft.EntityFrameworkCore.Migrations;

namespace Bsft.Data.SqlServer;

/// <summary>
/// Migration operations creating and maintaining the SQL Server full-text catalogue and indexes.
/// </summary>
/// <remarks>
/// Index creation belongs in the migration flow rather than in a manual step that gets forgotten in
/// one environment. The framework still never runs these by itself: they are emitted into the
/// application's own migration, which owns its schema exactly as it does for every other table.
/// <para>
/// Every statement here is emitted with <c>suppressTransaction</c>, because SQL Server refuses
/// full-text DDL inside a user transaction (Msg 574) and EF wraps a migration in one. The cost is
/// that these statements are not rolled back when a later operation of the same migration fails: the
/// catalogue and the index survive, while the rest of the migration is undone and EF still considers
/// the migration unapplied. Creating the catalogue tolerates that — it is guarded by an existence
/// check — but creating the index is not, so a re-run reports that the index already exists and the
/// leftover has to be dropped first.
/// </para>
/// </remarks>
public static class SqlServerFullTextMigrationBuilderExtensions
{
    /// <summary>
    /// Creates a full-text catalogue if it does not exist yet.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="catalogName">The catalogue name.</param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the name is not a valid identifier.</exception>
    public static MigrationBuilder CreateFullTextCatalog(this MigrationBuilder migrationBuilder, string catalogName)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));

        migrationBuilder.Sql(FullTextStatements.CreateCatalog(catalogName), suppressTransaction: true);
        return migrationBuilder;
    }

    /// <summary>
    /// Creates a full-text index over the given columns.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="command">The index to create.</param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when an identifier is invalid, no column is given, or the request's language does not
    /// map to a known locale.
    /// </exception>
    public static MigrationBuilder CreateFullTextIndex(
        this MigrationBuilder migrationBuilder,
        SqlServerCreateFullTextIndexRequest command)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        migrationBuilder.Sql(FullTextStatements.CreateIndex(
            command.Table,
            command.KeyIndexName,
            command.CatalogName,
            command.Columns,
            command.Language),
            suppressTransaction: true);

        return migrationBuilder;
    }

    /// <summary>
    /// Attaches a stopword list to a full-text index.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="table">The table whose index is altered.</param>
    /// <param name="stopList">
    /// The name of a stoplist object, or <c>SYSTEM</c> for the server's own list and <c>OFF</c> to
    /// index every word. Stopwords are language-specific, so a German index usually wants its own list
    /// rather than the default.
    /// </param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an identifier is invalid.</exception>
    public static MigrationBuilder SetFullTextStopList(this MigrationBuilder migrationBuilder, string table, string stopList)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));

        migrationBuilder.Sql(FullTextStatements.SetStopList(table, stopList), suppressTransaction: true);
        return migrationBuilder;
    }

    /// <summary>
    /// Drops a full-text index.
    /// </summary>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="table">The table whose index is dropped.</param>
    /// <returns>The same migration builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the table name is invalid.</exception>
    public static MigrationBuilder DropFullTextIndex(this MigrationBuilder migrationBuilder, string table)
    {
        if (migrationBuilder is null)
            throw new ArgumentNullException(nameof(migrationBuilder));

        migrationBuilder.Sql(FullTextStatements.DropIndex(table), suppressTransaction: true);
        return migrationBuilder;
    }
}
