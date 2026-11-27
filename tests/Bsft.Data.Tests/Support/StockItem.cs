namespace Bsft.Data.Tests.Support;

/// <summary>A row of the stock module, written through <see cref="StockContext"/>; its SKU is unique.</summary>
public sealed class StockItem
{
    public int Id { get; set; }

    public string Sku { get; set; } = string.Empty;
}
