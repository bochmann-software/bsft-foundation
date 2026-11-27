using Bsft.Core;
using Bsft.Data.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bsft.Data.KeysetPaging;

/// <summary>
/// Reads a keyset page through EF Core (see <see cref="IKeysetPageReader"/>).
/// </summary>
internal sealed class KeysetPageReader : IKeysetPageReader
{
    private const string PageSizeMessage = "The page size must be at least 1.";

    private readonly IKeysetCursorSerializer _serializer;

    private readonly IKeysetQueryComposer _composer;

    private readonly IOptions<BsftDataOptions> _options;

    /// <summary>Creates the reader.</summary>
    /// <param name="serializer">Reads the request's cursor and writes the next one.</param>
    /// <param name="composer">Narrows and orders the query.</param>
    /// <param name="options">Carries the largest page.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public KeysetPageReader(IKeysetCursorSerializer serializer, IKeysetQueryComposer composer, IOptions<BsftDataOptions> options)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<CursorPagedResult<TItem>> ListAsync<TItem, TKey, TId>(
        IQueryable<TItem> query,
        KeysetOrder<TItem, TKey, TId> order,
        CursorPagedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(request);

        // The page size comes from the client: below 1 is its mistake (a 400), above the maximum it is
        // clamped, as the offset query surface clamps. The validated maximum leaves room in Take's int for
        // the one row beyond the page that tells whether another follows.
        if (request.PageSize < 1)
            throw CreateInvalidPageSizeException();
        var pageSize = Math.Min(request.PageSize, _options.Value.KeysetPaging.MaxPageSize);
        if (!_serializer.Supports(typeof(TKey)) || !_serializer.Supports(typeof(TId)))
            throw new NotSupportedException(
                $"A keyset cursor cannot carry a {typeof(TKey).Name} sort key and a {typeof(TId).Name} id.");

        // Read, and so refused, before anything reaches the database.
        var after = request.Cursor is { } cursor
            ? _serializer.Deserialize<TKey, TId>(order.Listing, cursor)
            : null;

        long? totalCount = request.IncludeTotalCount
            ? await query.LongCountAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var rows = await _composer.Compose(query, order, after)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            rows.RemoveAt(pageSize);
            nextCursor = _serializer.Serialize(order.Listing, order.GetPosition(rows[^1]));
        }

        return new CursorPagedResult<TItem>(rows, pageSize, nextCursor) { TotalCount = totalCount };
    }

    private static ValidationException CreateInvalidPageSizeException() =>
        new(
            PageSizeMessage,
            new Dictionary<string, string[]> { ["pageSize"] = [PageSizeMessage] },
            KeysetPagingErrorCodes.InvalidPageSize);
}
