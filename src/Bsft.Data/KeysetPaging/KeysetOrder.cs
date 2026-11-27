using System.Linq.Expressions;
using Bsft.Data.Contracts.Pagination;

namespace Bsft.Data.KeysetPaging;

/// <summary>
/// The fixed order of a keyset listing: a sort key, the id that breaks ties between equal sort keys, the
/// direction both run in, and the name of the listing its cursors belong to.
/// </summary>
/// <typeparam name="TItem">The type of the rows the listing returns.</typeparam>
/// <typeparam name="TKey">The type of the sort key.</typeparam>
/// <typeparam name="TId">The type of the id; unique within the listing.</typeparam>
/// <remarks>
/// <para>
/// Declare one per listing and keep it, typically in a <c>static readonly</c> field: the selectors are
/// compiled once, here, to read the position of a page's last row.
/// </para>
/// <para>
/// The id must be unique within the listing, otherwise two rows with the same key and id are one position
/// and the second is skipped. Neither selector may be of a nullable value type; a string sort key must not
/// be null either, because databases disagree on where null sorts and the predicate cannot step over it.
/// Coalesce a nullable column in the selector if needed. The key must also be a column the database orders
/// natively: a provider that cannot order by a type (SQLite and <see cref="decimal"/>, for one) refuses the
/// query rather than paging wrongly.
/// </para>
/// </remarks>
public sealed class KeysetOrder<TItem, TKey, TId>
{
    private readonly Func<TItem, TKey> _keyOf;

    private readonly Func<TItem, TId> _idOf;

    /// <summary>Creates the order of a listing.</summary>
    /// <param name="listing">
    /// The name of the listing, such as <c>orders.by-created</c>. A cursor carries a checksum over it, so a
    /// cursor of another listing is refused instead of being read as a position of this one. Changing it
    /// invalidates the cursors clients hold.
    /// </param>
    /// <param name="key">Selects the sort key of a row.</param>
    /// <param name="id">Selects the id of a row, the tie-breaker.</param>
    /// <param name="direction">The direction of both the sort key and the id. Ascending by default.</param>
    /// <exception cref="ArgumentNullException">Thrown when a selector is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="listing"/> is blank, when <paramref name="direction"/> is not defined, or
    /// when a selector is of a nullable value type.
    /// </exception>
    public KeysetOrder(
        string listing,
        Expression<Func<TItem, TKey>> key,
        Expression<Func<TItem, TId>> id,
        SortDirection direction = SortDirection.Asc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listing);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(id);
        if (!Enum.IsDefined(direction))
            throw new ArgumentException($"The sort direction {direction} is not defined.", nameof(direction));
        if (Nullable.GetUnderlyingType(typeof(TKey)) is not null)
            throw new ArgumentException("The sort key of a keyset listing cannot be nullable; coalesce it in the selector.", nameof(key));
        if (Nullable.GetUnderlyingType(typeof(TId)) is not null)
            throw new ArgumentException("The id of a keyset listing cannot be nullable.", nameof(id));

        Listing = listing;
        Key = key;
        Id = id;
        Direction = direction;
        _keyOf = key.Compile();
        _idOf = id.Compile();
    }

    /// <summary>Gets the name of the listing the cursors belong to.</summary>
    public string Listing { get; }

    /// <summary>Gets the selector of the sort key.</summary>
    public Expression<Func<TItem, TKey>> Key { get; }

    /// <summary>Gets the selector of the id, the tie-breaker.</summary>
    public Expression<Func<TItem, TId>> Id { get; }

    /// <summary>Gets the direction of both the sort key and the id.</summary>
    public SortDirection Direction { get; }

    /// <summary>Reads the position of a row, the place the page after it starts from.</summary>
    /// <param name="item">The row.</param>
    /// <returns>The sort key and the id of the row.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
    public KeysetPosition<TKey, TId> GetPosition(TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new KeysetPosition<TKey, TId>(_keyOf(item), _idOf(item));
    }
}
