using Bsft.Data.Filtering;
using Bsft.Data.Search;
using System.Linq.Expressions;
using System.Reflection;
using Bsft.Data.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bsft.Data.SqlServer;

/// <summary>
/// Translates a search into SQL Server full-text predicates (<c>CONTAINS</c> / <c>FREETEXT</c>).
/// </summary>
/// <remarks>
/// <para>
/// The predicates are composed onto the caller's <see cref="IQueryable{T}"/>, never issued as a
/// standalone statement against the table. That is deliberate: EF Core global query filters — the
/// mechanism that enforces tenant isolation in <c>Bsft.Data</c> — apply to a composed query and would
/// be bypassed by a query built beside them.
/// </para>
/// <para>
/// The relevance score is a weighted field-match score: every field that matches contributes its
/// declared weight. It is translatable, stable and orderable, but it is not SQL Server's own
/// <c>CONTAINSTABLE</c> rank — see docs/search.md.
/// </para>
/// </remarks>
public sealed class SqlServerSearchProvider : SearchProviderBase
{
    private static readonly MethodInfo _contains = Resolve(nameof(SqlServerDbFunctionsExtensions.Contains), withLanguage: false);
    private static readonly MethodInfo _containsWithLanguage = Resolve(nameof(SqlServerDbFunctionsExtensions.Contains), withLanguage: true);
    private static readonly MethodInfo _freeText = Resolve(nameof(SqlServerDbFunctionsExtensions.FreeText), withLanguage: false);
    private static readonly MethodInfo _freeTextWithLanguage = Resolve(nameof(SqlServerDbFunctionsExtensions.FreeText), withLanguage: true);

    // EF.Functions answers the same instance every time, so the node it is wrapped in never changes
    // after start-up, and building a query is a per-request path.
    private static readonly ConstantExpression _functions = Expression.Constant(EF.Functions);

    private readonly BsftDataSqlServerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerSearchProvider"/> class.
    /// </summary>
    /// <param name="options">Declares which groups are backed by an index.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public SqlServerSearchProvider(IOptions<BsftDataSqlServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    /// <inheritdoc />
    public override IQueryable<RankedItem<TEntity>> ApplySearch<TEntity>(IQueryable<TEntity> source, ResolvedSearchRequest query)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);

        var parameter = Expression.Parameter(typeof(TEntity), "e");

        Expression? anyMatch = null;
        Expression score = Expression.Constant(0.0);

        foreach (var (predicate, weight) in BuildMatches(query, parameter))
        {
            anyMatch = anyMatch is null ? predicate : Expression.OrElse(anyMatch, predicate);
            score = Expression.Add(
                score,
                Expression.Condition(predicate, Expression.Constant(weight), Expression.Constant(0.0)));
        }

        return source
            .Where(Expression.Lambda<Func<TEntity, bool>>(anyMatch!, parameter))
            .Select(BuildRankedProjection<TEntity>(parameter, score));
    }

    /// <summary>
    /// Builds the full-text predicate for every declared field, each with the weight it contributes.
    /// </summary>
    private static IEnumerable<(Expression Predicate, double Weight)> BuildMatches(
        ResolvedSearchRequest query,
        ParameterExpression parameter)
    {
        var term = new SearchTerm(BuildSearchCondition(query.Term, query.Mode));
        var lcid = FullTextLanguageResolver.Resolve(query.Group.Language);

        foreach (var field in query.Group.Fields)
        {
            var column = QuerySorting.Rebind(field.Selector.Body, field.Selector.Parameters[0], parameter);
            yield return (BuildMatch(column, term, query.Mode, lcid), field.Weight);
        }
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

    /// <inheritdoc />
    protected override IEnumerable<string> Describe(SearchGroupConfiguration group)
    {
        if (!_options.IndexedGroups.Contains(group.Name))
        {
            yield return $"Search group '{group.Name}' on {group.EntityType.Name} has no full-text index. "
                + $"Create one and declare it with HasFullTextIndex(\"{group.Name}\").";
        }

        // A code this provider cannot map leaves the query without a LANGUAGE clause, so German text
        // would be stemmed by whatever the index happens to default to. Nothing in the results says
        // so, which makes it exactly the kind of quiet degradation the feature exists to remove.
        if (FullTextLanguageResolver.Resolve(group.Language) is null)
        {
            var supported = string.Join(", ", FullTextLanguageResolver.SupportedCodes);

            yield return $"Search group '{group.Name}' declares language '{group.Language}', which does not map to a "
                + $"SQL Server locale identifier, so no stemming language would be requested. Use one of: {supported}.";
        }
    }

    /// <summary>
    /// Builds the search condition. The prefix anchor goes on the right; a leading wildcard is never
    /// produced, because that is precisely the unindexable pattern this provider exists to replace.
    /// </summary>
    internal static string BuildSearchCondition(string term, SearchMode mode)
    {
        if (string.IsNullOrWhiteSpace(term))
            throw new ArgumentException("A search term is required.", nameof(term));

        // FREETEXT takes the phrase as-is; CONTAINS needs a quoted prefix term. Double quotes are
        // stripped rather than escaped so a stray quote cannot produce a malformed predicate.
        var cleaned = term.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();

        // Quotes and asterisks are syntax to the full-text engine, not content: the first delimits
        // the phrase, the second is the prefix anchor this method appends itself. SQL Server accepts
        // that anchor only as the last character of the quoted term, so an asterisk the user typed
        // cannot be carried into a prefix term at all: "AC*DC" would be anchored into "AC*DC*" and
        // fail on the server as a syntax error rather than search for anything. It becomes a
        // separator instead, which keeps both words the user typed and anchors the last of them.
        var content = Collapse(cleaned.Replace('*', ' '));

        // A term built from nothing but that syntax carries no term at all — "*", the intuitive
        // match-everything input, leaves nothing behind, and FREETEXT would take the same term as a
        // phrase and quietly match nothing. It is the same "no usable term" case as an empty string,
        // so both modes reject it the same way.
        if (content.Length == 0)
            throw new ArgumentException("A search term is required.", nameof(term));

        // Free text goes through as typed: FREETEXT has no operators to be broken by an asterisk,
        // and reading it as one would change what the user asked for.
        return mode is SearchMode.Prefix ? $"\"{content}*\"" : cleaned;
    }

    /// <summary>Reduces every run of whitespace to a single space and trims the ends.</summary>
    /// <param name="term">The term to normalize.</param>
    /// <returns>The normalized term, which is empty when the term carried no word.</returns>
    private static string Collapse(string term) =>
        string.Join(' ', term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Expression BuildMatch(Expression column, SearchTerm term, SearchMode mode, int? lcid)
    {
        // Member access on a boxed value, not Expression.Constant: EF Core inlines a bare constant
        // into the SQL text, so every distinct search term would produce its own statement and its
        // own query plan. Reading the term off an object turns it into a real SQL parameter.
        var condition = Expression.Property(Expression.Constant(term), nameof(SearchTerm.Value));

        if (lcid is { } language)
        {
            var method = mode is SearchMode.Prefix ? _containsWithLanguage : _freeTextWithLanguage;
            return Expression.Call(method, _functions, column, condition, Expression.Constant(language));
        }

        return Expression.Call(mode is SearchMode.Prefix ? _contains : _freeText, _functions, column, condition);
    }

    private static MethodInfo Resolve(string name, bool withLanguage)
    {
        Type[] parameters = withLanguage
            ? [typeof(DbFunctions), typeof(string), typeof(string), typeof(int)]
            : [typeof(DbFunctions), typeof(string), typeof(string)];

        return typeof(SqlServerDbFunctionsExtensions).GetMethod(name, parameters)!;
    }
}
