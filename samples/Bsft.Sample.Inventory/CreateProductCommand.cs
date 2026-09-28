using Bsft.Mediator;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Command to create a new product in the inventory. Returns the generated product identifier.
/// </summary>
/// <param name="Name">The name of the product.</param>
/// <param name="StockQuantity">The initial stock quantity.</param>
/// <param name="Price">The unit price of the product.</param>
public record CreateProductCommand(string Name, int StockQuantity, decimal Price) : ICommand<Guid>;
