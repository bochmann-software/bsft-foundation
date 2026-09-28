namespace Bsft.Schema;

/// <summary>The six types a declared schema may say a value has.</summary>
/// <remarks>
/// The whole list, deliberately. JSON Schema's <c>null</c> type, union types and the absence of a
/// type are all outside the subset: a configuration field whose type depends on what somebody sent is
/// a field nothing downstream can read without asking again.
/// </remarks>
public enum SchemaValueKind
{
    /// <summary>A JSON string.</summary>
    String,

    /// <summary>A JSON number with no fractional part.</summary>
    Integer,

    /// <summary>A JSON number.</summary>
    Number,

    /// <summary>A JSON boolean.</summary>
    Boolean,

    /// <summary>A JSON array, whose items are described by one schema.</summary>
    Array,

    /// <summary>A JSON object with named properties and no others.</summary>
    Object,
}
