namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>A row of the catalog module schema.</summary>
public sealed class CatalogItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
