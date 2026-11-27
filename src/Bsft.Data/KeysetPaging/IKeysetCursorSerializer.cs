namespace Bsft.Data.KeysetPaging;

/// <summary>
/// Turns a <see cref="KeysetPosition{TKey, TId}"/> into the opaque cursor a client holds, and back.
/// </summary>
/// <remarks>
/// <para>
/// The cursor is base64url without padding over a versioned binary payload: the format version, a type
/// code and the invariant text of the sort key and of the id, and a checksum over the payload and the name
/// of the listing. Reading checks all of it before any query runs: a cursor that is malformed, altered, of
/// an unknown version, of other key types or of another listing is refused with a
/// <see cref="Bsft.Core.ValidationException"/> keyed on <c>cursor</c> that carries
/// <see cref="KeysetPagingErrorCodes.InvalidCursor"/>.
/// </para>
/// <para>
/// The checksum is not a signature. It catches corruption, editing and cursors of another listing, but a
/// client that recomputes it can name any position of a listing; that grants nothing, because a position
/// only says where a page starts in rows the query already allows the caller to read.
/// </para>
/// <para>
/// Supported key and id types: <see cref="string"/>, <see cref="Guid"/>, <see cref="int"/>,
/// <see cref="long"/>, <see cref="decimal"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/> and
/// <see cref="DateOnly"/>.
/// </para>
/// <para>
/// Use it directly where a reader composes its own keyset query; <see cref="IKeysetPageReader"/> uses it
/// for the common case.
/// </para>
/// </remarks>
public interface IKeysetCursorSerializer
{
    /// <summary>Tells whether a type can be the sort key or the id of a cursor.</summary>
    /// <param name="valueType">The type.</param>
    /// <returns><see langword="true"/> when the type is supported.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueType"/> is null.</exception>
    bool Supports(Type valueType);

    /// <summary>Writes the cursor of a position.</summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <typeparam name="TId">The type of the id.</typeparam>
    /// <param name="listing">The name of the listing the cursor belongs to.</param>
    /// <param name="position">The position of the last row of a page.</param>
    /// <returns>The opaque cursor.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="position"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="listing"/> is blank or the position holds a null value.
    /// </exception>
    /// <exception cref="NotSupportedException">Thrown when a type is not supported.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the cursor would be longer than a cursor may be, because the sort key and the id together are too long.
    /// </exception>
    string Serialize<TKey, TId>(string listing, KeysetPosition<TKey, TId> position);

    /// <summary>Reads the position a cursor names, after checking that the cursor is valid.</summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <typeparam name="TId">The type of the id.</typeparam>
    /// <param name="listing">The name of the listing the cursor must belong to.</param>
    /// <param name="cursor">The cursor a client sent.</param>
    /// <returns>The position.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cursor"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="listing"/> is blank.</exception>
    /// <exception cref="NotSupportedException">Thrown when a type is not supported.</exception>
    /// <exception cref="Bsft.Core.ValidationException">
    /// Thrown when the cursor is not valid for the listing and the types; carries
    /// <see cref="KeysetPagingErrorCodes.InvalidCursor"/>.
    /// </exception>
    KeysetPosition<TKey, TId> Deserialize<TKey, TId>(string listing, string cursor);
}
