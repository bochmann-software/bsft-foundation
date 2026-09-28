namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Published by the saga timeout dispatcher when the inventory module has not answered a stock
/// reservation request in time. The saga treats it as an alternative to
/// <see cref="StockReservationResultEvent"/> and escalates instead of waiting forever.
/// </summary>
/// <param name="OrderId">The order whose reservation went unanswered.</param>
public record StockReservationTimedOutEvent(Guid OrderId);
