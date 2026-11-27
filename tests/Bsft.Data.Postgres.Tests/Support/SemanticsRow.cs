using System.Text.Json;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Test entity for filter semantics verification, carrying a JsonDocument custom field.
/// </summary>
public class SemanticsRow
{
    /// <summary>Gets or sets the row ID.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the optional size value.</summary>
    public int? Size { get; set; }

    /// <summary>Gets or sets the optional label.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets the custom JSON document (NOT NULL).</summary>
    public JsonDocument Custom { get; set; } = null!;
}
