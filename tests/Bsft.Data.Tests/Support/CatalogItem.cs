namespace Bsft.Data.Tests.Support;

/// <summary>A row of the catalog module, written through <see cref="CatalogContext"/>.</summary>
public sealed class CatalogItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
