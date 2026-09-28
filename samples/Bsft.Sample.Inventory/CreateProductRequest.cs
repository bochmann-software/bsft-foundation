namespace Bsft.Sample.Inventory;

/// <summary>
/// Request body for creating a product.
/// </summary>
/// <param name="Name">The name of the product.</param>
/// <param name="StockQuantity">The initial stock quantity.</param>
/// <param name="Price">The unit price of the product.</param>
public record CreateProductRequest(string Name, int StockQuantity, decimal Price);
