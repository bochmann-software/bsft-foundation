namespace Bsft.Data.KeysetPaging;

/// <summary>The machine-readable discriminators the refusals of keyset paging carry.</summary>
/// <remarks>
/// A client branches on these, so renaming one is a breaking change even though nothing stops compiling.
/// </remarks>
public static class KeysetPagingErrorCodes
{
    /// <summary>
    /// The cursor is malformed, was altered, has an unknown version, or belongs to another listing. Carried
    /// by a <see cref="Bsft.Core.ValidationException"/> keyed on <c>cursor</c>, which the host answers with
    /// a 400.
    /// </summary>
    public const string InvalidCursor = "KEYSET_INVALID_CURSOR";

    /// <summary>
    /// The page size is below 1. Carried by a <see cref="Bsft.Core.ValidationException"/> keyed on
    /// <c>pageSize</c>, a 400. A page size above the maximum is clamped, not refused.
    /// </summary>
    public const string InvalidPageSize = "KEYSET_INVALID_PAGE_SIZE";
}
