using Bsft.Data.Contracts.Pagination;

namespace Bsft.Data.Filtering;

/// <summary>
/// Executes paged, filtered, and sorted queries using a compiled query configuration.
/// </summary>
/// <typeparam name="TEntity">The entity type to query.</typeparam>
/// <typeparam name="TDto">The DTO type used for field mapping.</typeparam>
public sealed class QueryExecutor<TEntity, TDto> : IQueryExecutor<TEntity, TDto>
{
    private readonly QueryFieldMap<TEntity> _fieldMap;
    private readonly int _maxPageSize;
    private readonly string? _defaultSortField;
    private readonly SortDirection _defaultSortDirection;
    private readonly FilterStringMatch _stringMatch;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryExecutor{TEntity, TDto}"/> class.
    /// </summary>
    /// <param name="configuration">The query configuration defining field mappings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is null.</exception>
    public QueryExecutor(QueryConfigurationBase<TEntity, TDto> configuration)
    {
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        var builder = new QueryConfigurationBuilder<TEntity, TDto>();
        configuration.Configure(builder);

        _fieldMap = builder.BuildFieldMap();
        _maxPageSize = builder.GetMaxPageSize();
        _defaultSortField = builder.GetDefaultSortField();
        _defaultSortDirection = builder.GetDefaultSortDirection();
        _stringMatch = builder.GetStringMatch();
    }

    /// <inheritdoc />
    public async Task<PagedResult<TEntity>> ExecuteAsync(
        IQueryable<TEntity> query,
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.Filter is not null)
        {
            var predicate = FilterCompiler.Compile<TEntity>(request.Filter, _fieldMap, _stringMatch);
            query = query.Where(predicate);
        }

        query = ApplySort(query, request.Sort);

        var pageSize = Math.Min(request.PageSize, _maxPageSize);
        return await query.ToPagedResultAsync(request.Page, pageSize, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Orders the result before it is paged. Without an ordering, <c>Skip</c>/<c>Take</c> is at the
    /// mercy of the database's plan, so a row can appear on two pages or on none. A configuration
    /// that declares no default sort is no reason to page unordered, so the entity's own key breaks
    /// the tie — exactly as it does on the search path.
    /// </summary>
    private IQueryable<TEntity> ApplySort(IQueryable<TEntity> query, IReadOnlyList<SortItem>? sortItems)
    {
        if (sortItems is not null && sortItems.Count > 0)
            return ApplySortItems(query, sortItems);

        if (_defaultSortField is not null)
            return ApplySortItems(query, [new SortItem(_defaultSortField, _defaultSortDirection)]);

        return OrderByTieBreaker(query);
    }

    /// <summary>
    /// Orders by the entity's key, or — for an entity that follows no key convention — by the first
    /// declared sortable field. An entity with neither cannot be ordered from here, and the query is
    /// handed on as it came.
    /// </summary>
    private IQueryable<TEntity> OrderByTieBreaker(IQueryable<TEntity> query)
    {
        var tieBreaker = QuerySorting.ResolveTieBreaker<TEntity>(_fieldMap.GetFirstSortField());
        return tieBreaker is null ? query : QuerySorting.OrderBy(query, tieBreaker);
    }

    private IQueryable<TEntity> ApplySortItems(IQueryable<TEntity> query, IReadOnlyList<SortItem> sortItems) =>
        QuerySorting.Apply(query, sortItems, _fieldMap);
}
