namespace Bsft.Sample.Orders;

/// <summary>
/// Request body for creating an order. Only the product ID and quantity are needed;
/// the server resolves name and price from the inventory.
/// </summary>
/// <param name="ProductId">The identifier of the product to order.</param>
/// <param name="Quantity">The number of units to order.</param>
public record CreateOrderRequest(Guid ProductId, int Quantity);
