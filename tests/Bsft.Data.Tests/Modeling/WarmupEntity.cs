namespace Bsft.Data.Tests.Modeling;

/// <summary>The one entity of <see cref="FreshModelContext"/>.</summary>
public sealed class WarmupEntity
{
    /// <summary>Gets or sets the key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets a column, so the table has more than its key.</summary>
    public string? Name { get; set; }
}
