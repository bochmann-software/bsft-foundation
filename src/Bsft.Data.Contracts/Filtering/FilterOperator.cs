using System.Text.Json.Serialization;

namespace Bsft.Data.Contracts.Filtering;

/// <summary>
/// Defines the comparison operators available for query filtering.
/// </summary>
[JsonConverter(typeof(FilterOperatorJsonConverter))]
public enum FilterOperator
{
    /// <summary>Equals.</summary>
    Eq,

    /// <summary>Not equals.</summary>
    Neq,

    /// <summary>Greater than.</summary>
    Gt,

    /// <summary>Greater than or equal.</summary>
    Gte,

    /// <summary>Less than.</summary>
    Lt,

    /// <summary>Less than or equal.</summary>
    Lte,

    /// <summary>Contains (string).</summary>
    Contains,

    /// <summary>Does not contain (string).</summary>
    NotContains,

    /// <summary>Starts with (string).</summary>
    StartsWith,

    /// <summary>Does not start with (string).</summary>
    NotStartsWith,

    /// <summary>Ends with (string).</summary>
    EndsWith,

    /// <summary>Does not end with (string).</summary>
    NotEndsWith,

    /// <summary>
    /// Present — the field has a value (is not null). Unary: it takes no comparison value.
    /// Mirrors the SCIM (RFC 7644 §3.4.2.2) <c>pr</c> operator. Carried by <see cref="PresenceNode"/>
    /// rather than <see cref="ComparisonNode"/>, since presence compares against nothing.
    /// </summary>
    Present
}
