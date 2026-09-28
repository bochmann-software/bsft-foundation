using Bsft.Data.Search;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Bsft.Data.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using NpgsqlTypes;

namespace Bsft.Data.Postgres;

/// <summary>
/// Translates a search into a PostgreSQL <c>tsvector</c> match, composed onto the caller's queryable.
/// </summary>
/// <remarks>
/// <para>
/// The predicates are composed onto the caller's <see cref="IQueryable{T}"/>, never issued as a
/// standalone statement against the table. That is deliberate: EF Core global query filters — the
/// mechanism that enforces tenant isolation in <c>Bsft.Data</c> — apply to a composed query and would
/// be bypassed by a query built beside them.
/// </para>
/// <para>
/// Matching is <c>@@</c> against the stored <c>tsvector</c> column, which is what the GIN index is
/// built on, and the relevance score is PostgreSQL's own <c>ts_rank</c>. The declared field weights
/// reach it through the weight vector, paired with the <c>setweight</c> labels the migration helper
/// puts on the indexed column — see docs/search.md.
/// </para>
/// <para>
/// The <c>tsquery</c> is built once but appears twice in the generated statement, in the <c>WHERE</c>
/// and again in the ranking projection, because EF Core translates those clauses independently. That
/// is accepted rather than worked around: the alternatives — a lateral join or a wrapping subquery —
/// would have to be composed beside the caller's queryable, which is the shape that drops global
/// query filters. Parsing a query string is cheap next to the index probe it guards.
/// </para>
/// </remarks>
public sealed class PostgresSearchProvider : SearchProviderBase
{
    private static readonly MethodInfo _toTsQuery = Resolve(nameof(NpgsqlFullTextSearchDbFunctionsExtensions.ToTsQuery), withConfiguration: false);
    private static readonly MethodInfo _toTsQueryWithConfiguration = Resolve(nameof(NpgsqlFullTextSearchDbFunctionsExtensions.ToTsQuery), withConfiguration: true);
    private static readonly MethodInfo _webSearch = Resolve(nameof(NpgsqlFullTextSearchDbFunctionsExtensions.WebSearchToTsQuery), withConfiguration: false);
    private static readonly MethodInfo _webSearchWithConfiguration = Resolve(nameof(NpgsqlFullTextSearchDbFunctionsExtensions.WebSearchToTsQuery), withConfiguration: true);

    private static readonly MethodInfo _matches = typeof(NpgsqlFullTextSearchLinqExtensions)
        .GetMethod(nameof(NpgsqlFullTextSearchLinqExtensions.Matches), [typeof(NpgsqlTsVector), typeof(NpgsqlTsQuery)])!;

    private static readonly MethodInfo _rank = typeof(NpgsqlFullTextSearchLinqExtensions)
        .GetMethod(nameof(NpgsqlFullTextSearchLinqExtensions.Rank), [typeof(NpgsqlTsVector), typeof(float[]), typeof(NpgsqlTsQuery)])!;

    /// <summary>
    /// <c>EF.Property</c> closed over the vector type, resolved once: the column is always a
    /// <see cref="NpgsqlTsVector"/>, so nothing about this lookup varies per search.
    /// </summary>
    private static readonly MethodInfo _property = typeof(EF)
        .GetMethod(nameof(EF.Property))!.MakeGenericMethod(typeof(NpgsqlTsVector));

    private readonly BsftDataPostgresOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresSearchProvider"/> class.
    /// </summary>
    /// <param name="options">Declares which groups are backed by a <c>tsvector</c> column.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public PostgresSearchProvider(IOptions<BsftDataPostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    /// <inheritdoc />
    public override IQueryable<RankedItem<TEntity>> ApplySearch<TEntity>(IQueryable<TEntity> source, ResolvedSearchRequest query)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);

        var vectorProperty = ResolveVectorProperty(source, query.Group);
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        var vector = Expression.Call(_property, parameter, Expression.Constant(vectorProperty));

        var tsQuery = BuildTsQuery(query);

        return source
            .Where(Expression.Lambda<Func<TEntity, bool>>(Expression.Call(_matches, vector, tsQuery), parameter))
            .Select(BuildRankedProjection<TEntity>(parameter, BuildScore(vector, tsQuery, query.Group)));
    }

    /// <inheritdoc />
    protected override IEnumerable<string> Describe(SearchGroupConfiguration group)
    {
        if (_options.FindVectorProperty(group.Name) is null)
        {
            yield return $"Search group '{group.Name}' on {group.EntityType.Name} has no tsvector column. "
                + $"Create one with a GIN index and declare it with HasFullTextIndex(\"{group.Name}\").";
        }

        // A code this provider cannot map leaves the query on the server's default_text_search_config,
        // so German text would be stemmed by whatever that happens to be. Nothing in the results says
        // so, which makes it exactly the kind of quiet degradation the feature exists to remove.
        if (_options.ResolveConfiguration(group.Language) is null)
        {
            var supported = string.Join(", ", _options.SupportedLanguageCodes);

            // The codes a deployment registered itself are as usable as the built-in ones, so the
            // remedy names both routes rather than sending someone back to the six shipped codes.
            var remedy = $"Use one of: {supported}, or register the deployment's own with "
                + $"UseTextSearchConfiguration(\"{group.Language}\", \"...\").";

            yield return $"Search group '{group.Name}' declares language '{group.Language}', which does not map to a "
                + $"PostgreSQL text search configuration, so the server default would decide the stemming. {remedy}";
        }

        // Ranking weights are the four labels A to D that setweight puts on the lexemes, so a fifth
        // field has nowhere to be weighted and would silently rank as if it were the least important.
        if (group.Fields.Count > FullTextStatements.MaxLabeledColumns)
        {
            var limit = $"PostgreSQL weights lexemes with the four labels A to D, so at most {FullTextStatements.MaxLabeledColumns} fields "
                + "can carry distinct weights. Split the group, or index the extra columns into one of the existing weights.";

            yield return $"Search group '{group.Name}' declares {group.Fields.Count} fields, but {limit}";
        }
    }

    /// <summary>
    /// Builds the query expression. The prefix anchor goes on the right; a leading wildcard is never
    /// produced, because that is precisely the unindexable pattern this provider exists to replace.
    /// </summary>
    /// <param name="term">The caller's search term.</param>
    /// <param name="mode">The resolved search mode.</param>
    /// <returns>The text handed to <c>to_tsquery</c> or <c>websearch_to_tsquery</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when the term carries no usable characters.</exception>
    /// <remarks>
    /// The two modes are sanitised differently because the two functions are: <c>to_tsquery</c> takes
    /// an operator expression and fails on anything it cannot parse, so prefix mode is rebuilt from
    /// the term's own words, while <c>websearch_to_tsquery</c> is defined to take whatever a user
    /// typed and is therefore given the term as it stands.
    /// </remarks>
    internal static string BuildSearchCondition(string term, SearchMode mode)
    {
        if (string.IsNullOrWhiteSpace(term))
            throw new ArgumentException("A search term is required.", nameof(term));

        // Quotes, asterisks and tsquery operators are syntax to the full-text engine, not content, so a
        // term built from nothing but those carries no word and is the same "no usable term" case as an
        // empty string. Both modes reject it the same way. An asterisk beside real content ("AC*DC")
        // stays in the term. The quotes come out before the words are read because prefix mode rebuilds
        // the query from them and a quote inside a word is noise there: someone typing mu"eller is
        // typing one word, not two.
        var trimmed = term.Trim();
        var words = Words(trimmed.Replace("\"", string.Empty, StringComparison.Ordinal));

        if (words.Count == 0)
            throw new ArgumentException("A search term is required.", nameof(term));

        // Every word is anchored, so "erfahrener schwei" finds "erfahrener schweisser" while typing.
        // They are ANDed, which is what makes each further keystroke narrow the result. Free text goes
        // through as typed: a quoted phrase is websearch_to_tsquery's own phrase operator, so stripping
        // the quotes here would silently turn an exact-phrase request into an or-of-words one.
        return mode is SearchMode.Prefix
            ? string.Join(" & ", words.Select(word => $"{word}:*"))
            : trimmed;
    }

    /// <summary>
    /// Splits a term into the words a tsquery can be built from. Everything that is not a letter or a
    /// digit is a separator rather than an escape problem: <c>to_tsquery</c> would read <c>&amp;</c>,
    /// <c>|</c>, <c>!</c> and <c>:</c> as operators and fail on an unbalanced one.
    /// </summary>
    private static IReadOnlyList<string> Words(string term)
    {
        var words = new List<string>();
        var word = new StringBuilder();

        foreach (var character in term)
        {
            if (char.IsLetterOrDigit(character))
            {
                word.Append(character);
                continue;
            }

            Flush(words, word);
        }

        Flush(words, word);
        return words;
    }

    private static void Flush(List<string> words, StringBuilder word)
    {
        if (word.Length == 0)
            return;

        words.Add(word.ToString());
        word.Clear();
    }

    /// <summary>Resolves the mapped <c>tsvector</c> property, refusing anything that cannot be one.</summary>
    private string ResolveVectorProperty<TEntity>(IQueryable<TEntity> source, SearchGroupConfiguration group)
    {
        var vectorProperty = _options.FindVectorProperty(group.Name)
            ?? throw new InvalidOperationException(
                $"Search group '{group.Name}' has no tsvector column declared. Startup validation reports this; "
                + $"declare the column with HasFullTextIndex(\"{group.Name}\").");

        var model = QueryRootLocator.FindModel(source.Expression)
            ?? throw new InvalidOperationException(
                $"The queryable handed to {nameof(PostgresSearchProvider)} is not an EF Core query, so its tsvector "
                + "column cannot be checked. Search the DbSet, not a materialised collection.");

        var entityType = model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped, so it cannot be searched.");

        RequireVectorProperty(entityType, vectorProperty);
        return vectorProperty;
    }

    private static void RequireVectorProperty(IEntityType entityType, string vectorProperty)
    {
        var property = entityType.FindProperty(vectorProperty)
            ?? throw new InvalidOperationException(
                $"{entityType.DisplayName()} has no property '{vectorProperty}'. Map the generated column, usually as a "
                + $"shadow property: entity.Property<{nameof(NpgsqlTsVector)}>(\"{vectorProperty}\").");

        if (property.ClrType != typeof(NpgsqlTsVector))
        {
            throw new InvalidOperationException(
                $"Property '{vectorProperty}' on {entityType.DisplayName()} is a {property.ClrType.Name}, not an "
                + $"{nameof(NpgsqlTsVector)}. The search matches against the indexed tsvector column, not against text.");
        }
    }

    private MethodCallExpression BuildTsQuery(ResolvedSearchRequest query)
    {
        // Member access on a boxed value, not Expression.Constant: EF Core inlines a bare constant
        // into the SQL text, so every distinct search term would produce its own statement and its
        // own query plan. Reading the term off an object turns it into a real SQL parameter.
        var term = new SearchTerm(BuildSearchCondition(query.Term, query.Mode));
        var condition = Expression.Property(Expression.Constant(term), nameof(SearchTerm.Value));
        var functions = Expression.Constant(EF.Functions);

        var configuration = _options.ResolveConfiguration(query.Group.Language);
        var prefix = query.Mode is SearchMode.Prefix;

        if (configuration is null)
        {
            // Startup validation refuses such a group, so this is the second lock rather than the
            // first: the query is still well-formed, it just runs on the server's default
            // configuration instead of a named one.
            return Expression.Call(prefix ? _toTsQuery : _webSearch, functions, condition);
        }

        return Expression.Call(
            prefix ? _toTsQueryWithConfiguration : _webSearchWithConfiguration,
            functions,
            Expression.Constant(configuration),
            condition);
    }

    /// <summary>
    /// Builds the <c>ts_rank</c> call, weighted by the group's declared field weights.
    /// </summary>
    /// <remarks>
    /// PostgreSQL takes the weights as the four-element vector <c>{D, C, B, A}</c>, and the migration
    /// helper labels the indexed columns A, B, C, D in the order the group declares its fields. So
    /// the first declared field's weight lands in the last slot, and a field the group does not
    /// declare weighs nothing.
    /// </remarks>
    private static Expression BuildScore(Expression vector, Expression tsQuery, SearchGroupConfiguration group)
    {
        var weights = new float[FullTextStatements.MaxLabeledColumns];

        for (var index = 0; index < group.Fields.Count && index < FullTextStatements.MaxLabeledColumns; index++)
        {
            weights[FullTextStatements.MaxLabeledColumns - 1 - index] = (float)group.Fields[index].Weight;
        }

        var vectorLiteral = Expression.NewArrayInit(typeof(float), weights.Select(w => Expression.Constant(w)));

        // ts_rank answers in real; the framework's score is a double, and the widening cast is what
        // keeps every provider's RankedItem the same shape.
        return Expression.Convert(Expression.Call(_rank, vector, vectorLiteral, tsQuery), typeof(double));
    }

    /// <summary>Projects an entity to its ranked hit.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="parameter">The entity being projected.</param>
    /// <param name="score">The expression computing its relevance.</param>
    /// <returns>The projection.</returns>
    private static Expression<Func<TEntity, RankedItem<TEntity>>> BuildRankedProjection<TEntity>(
        ParameterExpression parameter,
        Expression score) =>
        Expression.Lambda<Func<TEntity, RankedItem<TEntity>>>(
            RankedItemProjection.For<TEntity>(parameter, score), parameter);

    private static MethodInfo Resolve(string name, bool withConfiguration)
    {
        Type[] parameters = withConfiguration
            ? [typeof(DbFunctions), typeof(string), typeof(string)]
            : [typeof(DbFunctions), typeof(string)];

        return typeof(NpgsqlFullTextSearchDbFunctionsExtensions).GetMethod(name, parameters)!;
    }
}
