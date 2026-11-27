namespace Bsft.Data;

/// <summary>
/// The bounds of keyset (cursor) paging, for a host that registered <c>AddBsftDataKeysetPaging</c>.
/// </summary>
/// <remarks>
/// A nested group of <see cref="BsftDataOptions"/>, bound as part of its section
/// (<c>Bsft:Data:KeysetPaging</c>) where a host binds that section.
/// </remarks>
public sealed class KeysetPagingOptions
{
    /// <summary>
    /// Gets or sets the largest page a request gets. A larger page size is clamped to it, as the offset
    /// query surface clamps to its own maximum, and the page reports the size it applied. From 1 to
    /// <see cref="int.MaxValue"/> minus one; defaults to <see cref="DataDefaults.KeysetMaxPageSize"/>.
    /// </summary>
    public int MaxPageSize { get; set; } = DataDefaults.KeysetMaxPageSize;
}
