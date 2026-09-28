namespace Bsft.Data.Sqlite;

/// <summary>
/// Describes the FTS5 index a migration creates.
/// </summary>
/// <param name="IndexTable">The FTS5 table to create. It is a table, not an index on one.</param>
/// <param name="ContentTable">The table whose rows are indexed.</param>
/// <param name="KeyColumn">
/// The integer primary key of <paramref name="ContentTable"/>. An external-content index stores no
/// text of its own and addresses every row by this column as its <c>rowid</c>.
/// </param>
/// <param name="Columns">
/// The columns to index, in the order the search group declares its fields. The order is what
/// <c>bm25</c> weights positionally.
/// </param>
/// <remarks>
/// It deliberately names no tokenizer. Which tokenizer an index is built with is an application-wide
/// decision held by <see cref="BsftDataSqliteOptions.Tokenizer"/>, because it is also what startup
/// validation answers "can this language be stemmed" from, and that check knows only the application's
/// options. A per-index tokenizer stated here would be a second source for one fact: the index would be
/// created with it while validation kept certifying the other one — including certifying stemming for
/// an index that has none.
/// </remarks>
public sealed record SqliteCreateFullTextIndexRequest(
    string IndexTable,
    string ContentTable,
    string KeyColumn,
    IReadOnlyList<string> Columns);
