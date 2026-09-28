using Bsft.Data.Contracts.Search;
using Bsft.Data.Contracts.Pagination;

namespace Bsft.Data.Search;

/// <summary>
/// Executes an index-backed search that composes with the structured filtering, sorting and paging
/// of <see cref="PagedRequest"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type to search.</typeparam>
/// <typeparam name="TDto">The DTO type the query configuration is written against.</typeparam>
public interface ISearchExecutor<TEntity, TDto>
{
    /// <summary>
    /// Runs the search carried by <see cref="PagedRequest.Search"/> together with its filter, sort
    /// and paging, scoped to what the caller may see.
    /// </summary>
    /// <param name="query">The source queryable.</param>
    /// <param name="request">The paged request; its <see cref="PagedRequest.Search"/> must be set.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A page of scored hits.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the request carries no search, or names an undeclared group.</exception>
    Task<PagedResult<RankedItem<TEntity>>> SearchAsync(
        IQueryable<TEntity> query,
        PagedRequest request,
        CancellationToken cancellationToken = default);
}
