namespace Bsft.Schema;

/// <summary>The bounds a submitted schema and a submitted document are read within.</summary>
/// <remarks>
/// <para>
/// Both documents arrive from a party the platform does not trust, so neither is read without a
/// ceiling. A schema nested four hundred deep and a document with a million array items are inputs,
/// not accidents, and the refusal has to arrive as a refusal rather than as a stack overflow in a
/// request thread.
/// </para>
/// <para>
/// The defaults are sized for an application manifest, which is a form with a few dozen fields. A host
/// validating something larger raises them deliberately, which is the point of them being options.
/// </para>
/// </remarks>
public sealed class BsftSchemaOptions
{
    /// <summary>The configuration section these bind from.</summary>
    public const string SectionName = "Bsft:Schema";

    /// <summary>Gets or sets how deeply a schema or a document may nest. Default 8.</summary>
    public int MaxDepth { get; set; } = 8;

    /// <summary>Gets or sets how many properties one object may declare or carry. Default 100.</summary>
    public int MaxProperties { get; set; } = 100;

    /// <summary>Gets or sets how many nodes one schema may contain in total. Default 500.</summary>
    public int MaxSchemaNodes { get; set; } = 500;

    /// <summary>Gets or sets how many items one array in a document may carry. Default 1000.</summary>
    /// <remarks>
    /// A ceiling above whatever a schema's own <c>maxItems</c> says, so an array with no declared bound
    /// still has one.
    /// </remarks>
    public int MaxArrayItems { get; set; } = 1000;

    /// <summary>Gets or sets how long one pattern match may run. Default 100 milliseconds.</summary>
    /// <remarks>
    /// A second line of defence. Patterns are compiled with backtracking off, which is what actually
    /// removes the catastrophic case; the timeout covers a large input against a merely slow
    /// expression.
    /// </remarks>
    public TimeSpan PatternTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
}
