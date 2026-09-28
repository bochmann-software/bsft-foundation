using Bsft.Mediator;

namespace Bsft.Sample.Orders;

/// <summary>
/// Command to create a new order. The handler resolves product name and price
/// from the inventory, so the client only needs to specify what and how many.
/// </summary>
/// <param name="ProductId">The identifier of the product being ordered.</param>
/// <param name="Quantity">The number of units to order.</param>
public record CreateOrderCommand(
    Guid ProductId,
    int Quantity) : ICommand<Guid>;
