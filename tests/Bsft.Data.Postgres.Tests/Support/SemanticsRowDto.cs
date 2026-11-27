namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// DTO for SemanticsRow, mapping to declared fields Size and Label.
/// </summary>
public class SemanticsRowDto
{
    /// <summary>Gets or sets the size.</summary>
    public int? Size { get; set; }

    /// <summary>Gets or sets the label.</summary>
    public string? Label { get; set; }
}
