using Bsft.Data.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data;

/// <summary>
/// Extension methods for <see cref="IQueryable{T}"/> to support pagination.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Applies pagination to the queryable and returns a <see cref="PagedResult{T}"/>.
    /// </summary>
    /// <typeparam name="T">The element type of the queryable.</typeparam>
    /// <param name="query">The source queryable.</param>
    /// <param name="page">The page number (1-based).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A paged result containing the items and pagination metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="page"/> or <paramref name="pageSize"/> is less than 1.</exception>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be at least 1.");
        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be at least 1.");

        // Compute the offset in 64-bit to avoid silent int overflow (which would produce a negative
        // Skip argument and throw a confusing ArgumentOutOfRangeException from the LINQ provider).
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(page), "The requested page is too large to evaluate.");

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasNextPage = (long)page * pageSize < totalCount;

        return new PagedResult<T>(items, totalCount, page, pageSize, hasNextPage);
    }
}
