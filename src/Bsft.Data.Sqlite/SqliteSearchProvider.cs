using Bsft.Data.Search;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Bsft.Data.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Bsft.Data.Sqlite;

/// <summary>
/// Translates a search into a SQLite FTS5 query, joined onto the caller's queryable.
/// </summary>
/// <remarks>
/// <para>
/// The FTS5 index is a table of its own, so the search is a join rather than a predicate on a column:
/// the index is matched first and its rows are joined back onto the caller's queryable by
/// <c>rowid</c>. The caller's queryable is still the outer side of that join, which is the point —
/// EF Core global query filters, the mechanism enforcing tenant isolation in <c>Bsft.Data</c>, apply
/// to a composed query and would be bypassed by a statement built beside it.
/// </para>
/// <para>
/// The relevance score is FTS5's own <c>bm25</c>, weighted by the group's declared field weights, and
/// negated so that a higher score is a better match. Unlike the SQL Server provider, which can only
/// offer a weighted field-match score, this is the engine's real ranking function — see docs/search.md.
/// </para>
/// </remarks>
public sealed class SqliteSearchProvider : SearchProviderBase
{
    /// <summary>
    /// The open generic <c>EF.Property</c>, resolved once. Only its type argument varies per search,
    /// and reflection lookups do not belong in the path a search-as-you-type endpoint runs per
    /// keystroke.
    /// </summary>
    private static readonly MethodInfo _efProperty = typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly MethodInfo _bm25 = typeof(SqliteFullTextFunctions)
        .GetMethod(nameof(SqliteFullTextFunctions.Bm25))!;

    private readonly BsftDataSqliteOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteSearchProvider"/> class.
    /// </summary>
    /// <param name="options">Declares which groups are backed by an FTS5 index.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public SqliteSearchProvider(IOptions<BsftDataSqliteOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    /// <inheritdoc />
    public override IQueryable<RankedItem<TEntity>> ApplySearch<TEntity>(IQueryable<TEntity> source, ResolvedSearchRequest query)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);

        var index = ResolveIndex(source, query.Group);
        var key = ResolveKeyProperty(index.Model, typeof(TEntity));

        return source.Join(
            MatchedRows(source, index, query),
            KeySelector<TEntity>(key),
            row => row.RowId,
            RankedProjection<TEntity>(query.Group));
    }

    /// <inheritdoc />
    protected override IEnumerable<string> Describe(SearchGroupConfiguration group)
    {
        if (_options.FindIndexTable(group.Name) is null)
        {
            yield return $"Search group '{group.Name}' on {group.EntityType.Name} has no FTS5 index. "
                + $"Create one and declare it with HasFullTextIndex(\"{group.Name}\", \"{group.EntityType.Name}s_fts\").";
        }

        // FTS5 has exactly one stemmer and it is English. A German group is therefore searched by
        // exact tokens, which is a usable search but a narrower one than the declaration promises,
        // and nothing in the results would say so. The application either picks a tokenizer that
        // stems the language or accepts the trade-off by name.
        if (!FullTextTokenization.CanStem(group.Language, _options.Tokenizer)
            && !_options.IsUnstemmedAccepted(group.Language))
        {
            var stemmed = string.Join(", ", FullTextTokenization.StemmedCodes);

            var engine = $"its only stemmer is Porter and Porter is English, so of the declarable languages only "
                + $"{stemmed} is stemmed, and only with {nameof(SqliteTokenizer)}.{nameof(SqliteTokenizer.Porter)}.";

            var remedy = $"Searching '{group.Language}' unstemmed is a reasonable choice, but it has to be a stated "
                + $"one: call AcceptsUnstemmedLanguage(\"{group.Language}\").";

            yield return $"Search group '{group.Name}' declares language '{group.Language}', which SQLite FTS5 cannot "
                + $"stem: {engine} {remedy}";
        }
    }

    /// <summary>
    /// Builds the FTS5 query expression. The prefix anchor goes on the right; a leading wildcard is
    /// never produced, because that is precisely the unindexable pattern this provider exists to
    /// replace.
    /// </summary>
    /// <param name="term">The caller's search term.</param>
    /// <param name="mode">The resolved search mode.</param>
    /// <returns>An FTS5 query expression.</returns>
    /// <exception cref="ArgumentException">Thrown when the term carries no usable characters.</exception>
    /// <remarks>
    /// Every token is emitted as an FTS5 string literal, which neutralises the whole query syntax —
    /// <c>AND</c>, <c>NEAR</c>, colons, parentheses — so a term typed into a search box cannot be
    /// read as operators. Double quotes are stripped rather than escaped, exactly as in the SQL
    /// Server provider, so a stray quote cannot produce a malformed query.
    /// </remarks>
    internal static string BuildSearchCondition(string term, SearchMode mode)
    {
        if (string.IsNullOrWhiteSpace(term))
            throw new ArgumentException("A search term is required.", nameof(term));

        var cleaned = term.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();

        // Quotes and asterisks are syntax to the full-text engine, not content: the first delimits
        // the phrase, the second is the prefix anchor this method appends itself. A term built from
        // nothing but those carries no term at all — "*", the intuitive match-everything input,
        // would become the prefix query on an empty phrase, which FTS5 rejects as a syntax error,
        // and free text would take it as a phrase with no tokens and quietly match nothing. It is
        // the same "no usable term" case as an empty string, so both modes reject it the same way.
        // The probe is what decides; an asterisk beside real content ("AC*DC") stays in the term.
        var content = cleaned.Replace("*", string.Empty, StringComparison.Ordinal).Trim();

        if (content.Length == 0)
            throw new ArgumentException("A search term is required.", nameof(term));

        // A phrase in prefix mode anchors its last token, so "erfahrener schwei" finds
        // "erfahrener schweisser".
        if (mode is SearchMode.Prefix)
            return $"\"{cleaned}\"*";

        // Free text answers the same question SQL Server's FREETEXT does: any of these words, with
        // the best matches ranked first. FTS5's own default is AND between tokens, which answers a
        // narrower question than the caller asked.
        var tokens = cleaned
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Replace("*", string.Empty, StringComparison.Ordinal).Length > 0)
            .Select(t => $"\"{t}\"");

        return string.Join(" OR ", tokens);
    }

    /// <summary>
    /// Resolves the mapped FTS5 index for a group, checking everything about it that would otherwise
    /// fail deep inside the engine or, worse, rank silently wrong.
    /// </summary>
    private IEntityType ResolveIndex<TEntity>(IQueryable<TEntity> source, SearchGroupConfiguration group)
    {
        var indexTable = _options.FindIndexTable(group.Name)
            ?? throw new InvalidOperationException(
                $"Search group '{group.Name}' has no FTS5 index declared. Startup validation reports this; "
                + $"declare the index with HasFullTextIndex(\"{group.Name}\", \"…_fts\").");

        var model = QueryRootLocator.FindModel(source.Expression)
            ?? throw new InvalidOperationException(
                $"The queryable handed to {nameof(SqliteSearchProvider)} is not an EF Core query, so the FTS5 index "
                + $"'{indexTable}' cannot be reached. Search the DbSet, not a materialised collection.");

        var index = model.FindEntityType(indexTable)
            ?? throw new InvalidOperationException(
                $"The FTS5 table '{indexTable}' is not mapped. Call modelBuilder.HasBsftFullTextIndex(\"{indexTable}\", …) "
                + "in OnModelCreating; EF Core can only compose a query against a table it knows.");

        RequireMatchingColumns(index, group, indexTable);
        return index;
    }

    /// <summary>
    /// Refuses a group whose fields do not line up with the index's columns.
    /// </summary>
    /// <remarks>
    /// <c>bm25</c> takes its weights positionally, one per indexed column, and rejects any other
    /// count outright. A count that happens to match while the order differs is the dangerous case:
    /// the query runs and ranks by the wrong weights, which no result would reveal.
    /// </remarks>
    private static void RequireMatchingColumns(IEntityType index, SearchGroupConfiguration group, string indexTable)
    {
        if (index.FindAnnotation(ModelBuilderExtensions.ColumnsAnnotation)?.Value is not string[] columns)
        {
            throw new InvalidOperationException(
                $"The FTS5 table '{indexTable}' is mapped without its column list. Map it with "
                + "modelBuilder.HasBsftFullTextIndex, which records the columns the ranking weights are matched against.");
        }

        var fields = group.Fields.Select(f => f.Name).ToArray();

        if (!columns.SequenceEqual(fields, StringComparer.Ordinal))
        {
            var reason = "bm25 weights its columns positionally, so the two have to be the same names in the same "
                + "order or the ranking is silently wrong.";

            throw new InvalidOperationException(
                $"Search group '{group.Name}' declares the fields [{string.Join(", ", fields)}], but the FTS5 table "
                + $"'{indexTable}' is mapped with the columns [{string.Join(", ", columns)}]. {reason}");
        }
    }

    /// <summary>
    /// Resolves the primary key the index's <c>rowid</c> refers to.
    /// </summary>
    private static IProperty ResolveKeyProperty(IModel model, Type entityType)
    {
        var mapped = model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped, so it cannot be searched.");

        var key = mapped.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"{entityType.Name} has no primary key. An external-content FTS5 index addresses its rows by rowid, "
                + "which is the integer primary key of the table it indexes.");

        if (key.Properties.Count != 1)
        {
            throw new InvalidOperationException(
                $"{entityType.Name} has a composite primary key. An external-content FTS5 index addresses its rows by a "
                + "single integer rowid, so a composite key cannot be indexed this way.");
        }

        var property = key.Properties[0];

        if (property.ClrType != typeof(int) && property.ClrType != typeof(long))
        {
            throw new InvalidOperationException(
                $"{entityType.Name} has a {property.ClrType.Name} primary key. An external-content FTS5 index addresses "
                + "its rows by an integer rowid, so the key has to be an int or a long.");
        }

        return property;
    }

    /// <summary>Builds the matched-index side of the join.</summary>
    private static IQueryable<BsftFullTextIndexRow> MatchedRows<TEntity>(
        IQueryable<TEntity> source,
        IEntityType index,
        ResolvedSearchRequest query)
    {
        var provider = source.Provider as IAsyncQueryProvider
            ?? throw new InvalidOperationException(
                $"The queryable handed to {nameof(SqliteSearchProvider)} is not an EF Core query. Search the DbSet.");

        // A boxed value rather than a bare constant: EF Core inlines a constant into the SQL text, so
        // every distinct search term would produce its own statement and its own query plan. Reading
        // the condition off an object turns it into a real SQL parameter.
        var term = new SearchTerm(BuildSearchCondition(query.Term, query.Mode));

        return provider
            .CreateQuery<BsftFullTextIndexRow>(new EntityQueryRootExpression(provider, index))
            .Where(row => SqliteFullTextFunctions.IsMatch(term.Value, row.Match));
    }

    /// <summary>
    /// Selects the entity's key as the <c>rowid</c> it is joined by, through <c>EF.Property</c> so
    /// that a key mapped as a shadow property works like a declared one.
    /// </summary>
    private static Expression<Func<TEntity, long>> KeySelector<TEntity>(IProperty key)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        var access = Expression.Call(
            _efProperty.MakeGenericMethod(key.ClrType),
            parameter,
            Expression.Constant(key.Name));

        Expression body = key.ClrType == typeof(long) ? access : Expression.Convert(access, typeof(long));
        return Expression.Lambda<Func<TEntity, long>>(body, parameter);
    }

    /// <summary>
    /// Projects a matched pair to its ranked hit.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="group">The group whose field weights the score is computed from.</param>
    /// <returns>The projection.</returns>
    private static Expression<Func<TEntity, BsftFullTextIndexRow, RankedItem<TEntity>>> RankedProjection<TEntity>(
        SearchGroupConfiguration group)
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var row = Expression.Parameter(typeof(BsftFullTextIndexRow), "r");

        var weights = string.Join(",", group.Fields.Select(f => f.Weight.ToString(CultureInfo.InvariantCulture)));

        // bm25 is negative and more negative the better the match, so the negation is what makes the
        // score sort descending like every other provider's.
        var score = Expression.Negate(Expression.Call(
            _bm25,
            Expression.Property(row, nameof(BsftFullTextIndexRow.Match)),
            Expression.Constant(weights)));

        return Expression.Lambda<Func<TEntity, BsftFullTextIndexRow, RankedItem<TEntity>>>(
            RankedItemProjection.For<TEntity>(entity, score), entity, row);
    }
}
