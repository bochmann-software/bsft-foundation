namespace Bsft.Data.KeysetPaging;

/// <summary>
/// The place in a keyset listing a page ends at: the sort key and the id of its last row. The next page
/// starts strictly after it.
/// </summary>
/// <typeparam name="TKey">The type of the sort key.</typeparam>
/// <typeparam name="TId">The type of the id that breaks ties between equal sort keys.</typeparam>
/// <remarks>
/// A class named for the thing itself rather than a record named for a role (package-conventions, "Data
/// types"). Its properties are what the keyset predicate reads as query parameters.
/// </remarks>
public sealed class KeysetPosition<TKey, TId>
{
    /// <summary>Creates a position.</summary>
    /// <param name="key">The sort key of the last row.</param>
    /// <param name="id">The id of the last row.</param>
    public KeysetPosition(TKey key, TId id)
    {
        Key = key;
        Id = id;
    }

    /// <summary>Gets the sort key of the last row.</summary>
    public TKey Key { get; }

    /// <summary>Gets the id of the last row.</summary>
    public TId Id { get; }
}
