using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>
/// Creates the real FTS5 schema by running exactly the statements the migration helpers emit, so the
/// end-to-end tests prove the shipped DDL and not a hand-written copy of it.
/// </summary>
public static class FullTextSchema
{
    public static SqliteCreateFullTextIndexRequest Command { get; } =
        new("Candidates_fts", "Candidates", "Id", ["Name", "Notes"]);

    /// <param name="context">The context the statements are executed on.</param>
    /// <param name="rebuild">Whether to repopulate the index from the rows already in the table.</param>
    /// <param name="options">
    /// The application's search options, which is where the tokenizer's diacritic folding level comes
    /// from. Omitted, the framework's default applies — the level every other test here assumes.
    /// </param>
    public static void Apply(DbContext context, bool rebuild = false, BsftDataSqliteOptions? options = null)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
        builder.CreateFullTextIndex(Command, options).CreateFullTextSyncTriggers(Command);

        if (rebuild)
            builder.RebuildFullTextIndex(Command.IndexTable);

        foreach (var operation in builder.Operations.OfType<SqlOperation>())
        {
            context.Database.ExecuteSqlRaw(operation.Sql);
        }
    }
}
