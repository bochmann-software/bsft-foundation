namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Published by the saga to request stock reservation from the Inventory module.
/// Travels through the EventAggregator (not the mediator pipeline).
/// </summary>
/// <param name="OrderId">The order requesting the reservation.</param>
/// <param name="ProductId">The product to reserve stock for.</param>
/// <param name="Quantity">The number of units to reserve.</param>
public record StockReservationRequestedEvent(Guid OrderId, Guid ProductId, int Quantity);
