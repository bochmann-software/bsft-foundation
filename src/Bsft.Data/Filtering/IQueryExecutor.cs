using Bsft.Data.Contracts.Pagination;


namespace Bsft.Data.Filtering;

/// <summary>
/// Executes paged, filtered, and sorted queries against a queryable source.
/// </summary>
/// <typeparam name="TEntity">The entity type to query.</typeparam>
/// <typeparam name="TDto">The DTO type used for field mapping.</typeparam>
public interface IQueryExecutor<TEntity, TDto>
{
    /// <summary>
    /// Executes a paged query using the provided queryable source and request parameters.
    /// </summary>
    /// <param name="query">The source queryable.</param>
    /// <param name="request">The paged request containing filter, sort, and pagination parameters.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A paged result of entities.</returns>
    Task<PagedResult<TEntity>> ExecuteAsync(IQueryable<TEntity> query, PagedRequest request, CancellationToken cancellationToken = default);
}
