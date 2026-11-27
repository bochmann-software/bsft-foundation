namespace Bsft.Core;

/// <summary>The status of a code point in the UTS 46 mapping table; a code point the table does not list is disallowed.</summary>
internal enum IdnaStatus
{
    /// <summary>The code point stays as it is.</summary>
    Valid,

    /// <summary>The code point is replaced by its mapping.</summary>
    Mapped,

    /// <summary>The code point stays (non-transitional processing); transitional processing would replace it.</summary>
    Deviation,

    /// <summary>The code point is removed.</summary>
    Ignored,
}
