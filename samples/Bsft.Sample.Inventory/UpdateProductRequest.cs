namespace Bsft.Sample.Inventory;

/// <summary>
/// Request body for updating a product. It carries the editable state only: the identifier is part
/// of the route, and a body that disagreed with the route would leave the server to pick a winner
/// and the client to guess which it picked.
/// </summary>
/// <param name="Name">The new name of the product.</param>
/// <param name="StockQuantity">The new stock quantity.</param>
/// <param name="Price">The new unit price of the product.</param>
public record UpdateProductRequest(string Name, int StockQuantity, decimal Price);
