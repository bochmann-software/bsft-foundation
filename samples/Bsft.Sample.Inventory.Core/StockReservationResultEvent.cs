namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Published by the Inventory module after processing a stock reservation request.
/// The saga waits for this event to determine whether to confirm or cancel the order.
/// </summary>
/// <param name="OrderId">The order the reservation was requested for.</param>
/// <param name="IsSuccess">Whether the stock was successfully reserved.</param>
public record StockReservationResultEvent(Guid OrderId, bool IsSuccess);
