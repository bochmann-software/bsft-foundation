namespace Bsft.Data.KeysetPaging;

/// <summary>Narrows a query to the rows after a keyset position and puts it in the listing's order.</summary>
internal interface IKeysetQueryComposer
{
    /// <summary>Composes the keyset predicate and the order onto a query.</summary>
    /// <typeparam name="TItem">The type of the rows.</typeparam>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <typeparam name="TId">The type of the id.</typeparam>
    /// <param name="query">The filtered query.</param>
    /// <param name="order">The order of the listing.</param>
    /// <param name="after">The position the page starts after, or null for the first page.</param>
    /// <returns>The ordered query, narrowed to the rows after <paramref name="after"/>.</returns>
    IQueryable<TItem> Compose<TItem, TKey, TId>(
        IQueryable<TItem> query,
        KeysetOrder<TItem, TKey, TId> order,
        KeysetPosition<TKey, TId>? after);
}
