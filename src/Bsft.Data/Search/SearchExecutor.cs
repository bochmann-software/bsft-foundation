using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Search;
using System.Diagnostics;
using System.Linq.Expressions;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Diagnostics;

namespace Bsft.Data.Search;

/// <summary>
/// Default <see cref="ISearchExecutor{TEntity, TDto}"/>: scopes the queryable, hands it to the
/// search provider, composes filter, sort and paging, and reports the outcome.
/// </summary>
/// <remarks>
/// The executor is registered scoped so that the <see cref="ISearchScopeProvider"/> it depends on is
/// the caller's own. A provider derives the boundary from request state — the ambient
/// <see cref="MultiTenancy.ITenantContext"/>, the signed-in principal — and resolving it from a scope
/// the executor opened itself would hand it a blank request whose tenant is never set.
/// </remarks>
/// <typeparam name="TEntity">The entity type to search.</typeparam>
/// <typeparam name="TDto">The DTO type the query configuration is written against.</typeparam>
public sealed class SearchExecutor<TEntity, TDto> : ISearchExecutor<TEntity, TDto>
{
    private readonly CompiledSearchConfiguration<TEntity, TDto> _configuration;
    private readonly Dictionary<string, SearchGroupConfiguration> _groups;
    private readonly ISearchProvider? _searchProvider;
    private readonly ISearchScopeProvider? _scopeProvider;
    private readonly IDataTelemetry _telemetry;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchExecutor{TEntity, TDto}"/> class.
    /// </summary>
    /// <param name="configuration">The compiled query configuration.</param>
    /// <param name="searchProvider">
    /// The provider translating a search into an index-backed query, or <see langword="null"/> when
    /// the application registers none. An application that declares no search group needs no
    /// provider, so the dependency is optional here and refused only when a search is attempted.
    /// </param>
    /// <param name="telemetry">Telemetry sink for search metrics.</param>
    /// <param name="scopeProvider">
    /// The visibility boundary for the current request, or <see langword="null"/> when the
    /// application registers none. A null provider is refused for every group that is not declared
    /// <c>Unscoped()</c>, so the omission cannot read as "search everything".
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public SearchExecutor(
        CompiledSearchConfiguration<TEntity, TDto> configuration,
        IDataTelemetry telemetry,
        ISearchProvider? searchProvider = null,
        ISearchScopeProvider? scopeProvider = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _searchProvider = searchProvider;
        _scopeProvider = scopeProvider;

        _groups = configuration.Groups.ToDictionary(g => g.Name, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<PagedResult<RankedItem<TEntity>>> SearchAsync(
        IQueryable<TEntity> query,
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.Search is not { } search)
            throw new InvalidOperationException("The paged request carries no search.");
        if (!_groups.TryGetValue(search.Group, out var group))
            throw new InvalidOperationException($"Unknown search group: '{search.Group}'.");

        // Unreachable in a validated host: a declared group without a provider is exactly what
        // SearchStartupValidator refuses at startup. The check exists so the dependency can stay
        // optional — an application that declares no group must not have to register a provider it
        // never uses — without the omission ever degrading into a null reference here.
        if (_searchProvider is null)
        {
            throw new InvalidOperationException(
                $"Search group '{group.Name}' is declared but no {nameof(ISearchProvider)} is registered. "
                + "Add a provider package such as Bsft.Data.SqlServer.");
        }

        var started = Stopwatch.GetTimestamp();

        // Scoping happens before the provider sees the queryable, so no provider implementation can
        // return a row the caller may not see.
        var scope = await ResolveScopeAsync(group, cancellationToken).ConfigureAwait(false);
        query = SearchScopeFilter.Apply(query, scope, group);

        if (request.Filter is not null)
            query = query.Where(FilterCompiler.Compile<TEntity>(request.Filter, _configuration.FieldMap));

        if (!search.IsOrderedByRank)
            query = ApplySort(query, request.Sort, group);

        var hits = _searchProvider.ApplySearch(query, new ResolvedSearchRequest(group, search.Term, search.Mode ?? group.DefaultMode));

        if (search.IsOrderedByRank)
            hits = OrderByRank(hits, group);

        var pageSize = Math.Min(request.PageSize, _configuration.MaxPageSize);
        var result = await hits.ToPagedResultAsync(request.Page, pageSize, cancellationToken).ConfigureAwait(false);

        _telemetry.SearchCompleted(group.Name, Stopwatch.GetElapsedTime(started).TotalSeconds, result.TotalCount);
        return result;
    }

    /// <summary>
    /// Orders the result before it is paged. Without an ordering, <c>Skip</c>/<c>Take</c> is at the
    /// mercy of the database's plan, so a row can appear on two pages or on none. The configured
    /// default sort is the fallback, exactly as it is for a plain query.
    /// </summary>
    private IQueryable<TEntity> ApplySort(IQueryable<TEntity> query, IReadOnlyList<SortItem>? sortItems, SearchGroupConfiguration group)
    {
        if (sortItems is { Count: > 0 })
            return QuerySorting.Apply(query, sortItems, _configuration.FieldMap);

        if (_configuration.DefaultSortField is { } field)
            return QuerySorting.Apply(query, [new SortItem(field, _configuration.DefaultSortDirection)], _configuration.FieldMap);

        // An application that configured neither still pages, and handing Skip/Take an unordered
        // source is exactly the non-determinism this method exists to prevent. Fall back to the same
        // tie-breaker the ranked path uses, so both paths page by the same rule.
        return OrderByTieBreaker(query, group);
    }

    /// <summary>
    /// Orders a queryable by the group's tie-breaker, the last resort when nothing else establishes
    /// an order. Only reached with no default sort configured, so the entity key — or, absent a key,
    /// the first searchable field — is the fallback.
    /// </summary>
    private IQueryable<TEntity> OrderByTieBreaker(IQueryable<TEntity> query, SearchGroupConfiguration group) =>
        QuerySorting.OrderBy(query, QuerySorting.ResolveTieBreaker<TEntity>(group.Fields[0].Selector)!);

    private async Task<SearchScopeState> ResolveScopeAsync(SearchGroupConfiguration group, CancellationToken cancellationToken)
    {
        if (_scopeProvider is not null)
            return await _scopeProvider.GetScopeAsync(cancellationToken).ConfigureAwait(false);

        // Fail closed. A missing registration is indistinguishable from a deliberate choice at this
        // point, and treating it as "search everything" turns one forgotten DI line into a
        // cross-tenant dump. Startup validation catches this too; this is the second lock.
        if (!group.IsUnscoped)
        {
            throw new InvalidOperationException(
                $"Search group '{group.Name}' is scoped but no {nameof(ISearchScopeProvider)} is registered. "
                + "Register one, or — only if its entity carries no scoping marker — declare the group Unscoped().");
        }

        return SearchScopeState.Unrestricted;
    }

    /// <summary>
    /// Orders by score and breaks ties deterministically, so a hit does not move between pages. A
    /// configured default sort orders the score ties first — honoring the host's intent for the
    /// secondary order — and the entity's own unique key is the final tie-breaker beneath it, so a
    /// non-unique default sort still pages deterministically.
    /// </summary>
    private IQueryable<RankedItem<TEntity>> OrderByRank(IQueryable<RankedItem<TEntity>> hits, SearchGroupConfiguration group)
    {
        var expression = hits.OrderByDescending(r => r.Score).Expression;

        foreach (var step in ResolveRankTieBreakers(group))
            expression = AppendTieBreaker(expression, step);

        return hits.Provider.CreateQuery<RankedItem<TEntity>>(expression);
    }

    /// <summary>
    /// Appends one secondary sort to the ranked ordering, rebinding the entity-scoped selector onto
    /// the ranked hit's <see cref="RankedItem{TEntity}.Item"/> so it can order the wrapper.
    /// </summary>
    private static Expression AppendTieBreaker(Expression source, RankTieBreaker step)
    {
        var parameter = Expression.Parameter(typeof(RankedItem<TEntity>), "r");
        var item = Expression.Property(parameter, nameof(RankedItem<TEntity>.Item));
        var body = QuerySorting.Rebind(step.Selector.Body, step.Selector.Parameters[0], item);
        var methodName = step.Direction is SortDirection.Asc
            ? nameof(Queryable.ThenBy)
            : nameof(Queryable.ThenByDescending);

        return Expression.Call(
            typeof(Queryable),
            methodName,
            [typeof(RankedItem<TEntity>), body.Type],
            source,
            Expression.Quote(Expression.Lambda(body, parameter)));
    }

    /// <summary>
    /// Resolves the ordered secondary sort steps for the ranked path: a configured default sort first,
    /// then the entity's unique key as the final tie-breaker. Only when there is neither a default sort
    /// nor a key does the first searchable field stand in — configure a unique default sort for a
    /// strictly stable order in that case.
    /// </summary>
    private IReadOnlyList<RankTieBreaker> ResolveRankTieBreakers(SearchGroupConfiguration group)
    {
        var defaultSort = _configuration.DefaultSortField is { } field
            ? _configuration.FieldMap.GetSortField(field)
            : null;

        return QuerySorting.ResolveRankTieBreakers<TEntity>(
            defaultSort,
            _configuration.DefaultSortDirection,
            group.Fields[0].Selector);
    }
}
