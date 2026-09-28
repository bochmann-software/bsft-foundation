using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Bsft.Data.Sqlite;

/// <summary>
/// Model configuration making an FTS5 index visible to EF Core.
/// </summary>
/// <remarks>
/// An FTS5 index is a table, and EF Core can only compose a query against a table it knows. The
/// application therefore maps each index once, in the same <c>OnModelCreating</c> that maps
/// everything else it owns. This does not create anything — the index is created by the migration
/// helpers — and the mapping is excluded from migrations so that scaffolding never tries to.
/// </remarks>
public static class ModelBuilderExtensions
{
    /// <summary>The annotation carrying the indexed columns, so the provider can check its weights against them.</summary>
    internal const string ColumnsAnnotation = "Bsft:FullTextColumns";

    /// <summary>
    /// The format a column weight is written with. Seventeen significant digits is what a double
    /// round-trips through, so a weight the caller declared reaches the planner as the same number.
    /// </summary>
    private const string WeightFormat = "0.0###############";

    /// <summary>
    /// Maps an FTS5 index table and registers the FTS5 functions the search provider composes with.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="indexTable">The FTS5 table, as named when the migration created it.</param>
    /// <param name="columns">
    /// The indexed columns, in the order the index declares them. The order is not decoration:
    /// <c>bm25</c> takes one weight per column positionally, so a group whose fields are declared in
    /// a different order would be ranked by the wrong weights. The provider compares the two and
    /// refuses the mismatch rather than ranking quietly wrong.
    /// </param>
    /// <returns>The same model builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the table name is invalid or no column is given.</exception>
    public static ModelBuilder HasBsftFullTextIndex(this ModelBuilder modelBuilder, string indexTable, params string[] columns)
    {
        if (modelBuilder is null)
            throw new ArgumentNullException(nameof(modelBuilder));
        if (string.IsNullOrWhiteSpace(indexTable))
            throw new ArgumentException("An FTS5 table name is required.", nameof(indexTable));
        if (columns is null || columns.Length == 0)
            throw new ArgumentException("An FTS5 index has at least one column.", nameof(columns));

        modelBuilder.SharedTypeEntity<BsftFullTextIndexRow>(indexTable, entity =>
        {
            // Keyless: the index is only ever joined and never tracked, and its rowid is not a key EF
            // has any business caching identities by.
            entity.HasNoKey();
            entity.ToTable(indexTable, t => t.ExcludeFromMigrations());
            entity.Property(r => r.RowId).HasColumnName("rowid");

            // FTS5 names the hidden column after the table. That column is the operand of match()
            // and bm25(), which is the only reason it is mapped at all.
            entity.Property(r => r.Match).HasColumnName(indexTable);
            entity.HasAnnotation(ColumnsAnnotation, columns);
        });

        RegisterFunctions(modelBuilder);
        return modelBuilder;
    }

    private static void RegisterFunctions(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDbFunction(typeof(SqliteFullTextFunctions).GetMethod(nameof(SqliteFullTextFunctions.IsMatch))!)
            .HasTranslation(args => new SqlFunctionExpression(
                "match",
                args,
                nullable: false,
                argumentsPropagateNullability: [false, false],
                typeof(bool),
                typeMapping: null));

        modelBuilder
            .HasDbFunction(typeof(SqliteFullTextFunctions).GetMethod(nameof(SqliteFullTextFunctions.Bm25))!)
            .HasTranslation(TranslateBm25);
    }

    /// <summary>
    /// Expands the weight list into one SQL argument per indexed column, which is the only shape
    /// <c>bm25</c> accepts — it has no array parameter and rejects a count other than the index's
    /// column count.
    /// </summary>
    private static SqlExpression TranslateBm25(IReadOnlyList<SqlExpression> arguments)
    {
        if (arguments[1] is not SqlConstantExpression { Value: string weights })
        {
            throw new NotSupportedException(
                $"{nameof(SqliteFullTextFunctions.Bm25)} needs its column weights as a constant, because SQL has no "
                + "way to take them as a parameter. Only the search provider is meant to call it.");
        }

        var translated = new List<SqlExpression> { arguments[0] };

        foreach (var weight in weights.Split(','))
        {
            if (!double.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new NotSupportedException($"'{weight}' is not a column weight.");

            // A fragment rather than a constant: these are literal numbers in the statement text and
            // must not become parameters, because bm25 resolves its weights while planning.
            translated.Add(new SqlFragmentExpression(value.ToString(WeightFormat, CultureInfo.InvariantCulture)));
        }

        return new SqlFunctionExpression(
            "bm25",
            translated,
            nullable: false,
            argumentsPropagateNullability: translated.ConvertAll(_ => false),
            typeof(double),
            typeMapping: null);
    }
}
