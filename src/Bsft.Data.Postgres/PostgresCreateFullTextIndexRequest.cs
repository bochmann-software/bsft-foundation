namespace Bsft.Data.Postgres;

/// <summary>
/// Describes the <c>tsvector</c> column and GIN index a migration creates.
/// </summary>
/// <param name="Table">The table to index.</param>
/// <param name="Columns">
/// The text columns to index, in the order the search group declares its fields. The order is what
/// decides the weight label each column gets: the first is <c>A</c>, the second <c>B</c>, and so on,
/// which is how the group's declared field weights reach <c>ts_rank</c>.
/// </param>
/// <param name="VectorColumn">The generated column holding the indexed document.</param>
/// <param name="Language">
/// A two-letter language code such as <c>de</c> driving stemming and stopwords, or
/// <see langword="null"/> to take <see cref="BsftDataPostgresOptions.DefaultLanguage"/>. The regconfig
/// is baked into the generated column, so it is a per-index decision — naming it here overrides the
/// deployment's default for this one index.
/// </param>
public sealed record PostgresCreateFullTextIndexRequest(
    string Table,
    IReadOnlyList<string> Columns,
    string VectorColumn = BsftDataPostgresOptions.DefaultVectorProperty,
    string? Language = null);
