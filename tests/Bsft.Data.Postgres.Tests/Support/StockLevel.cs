namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>A row of the inventory module schema; its SKU is unique.</summary>
public sealed class StockLevel
{
    public int Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
