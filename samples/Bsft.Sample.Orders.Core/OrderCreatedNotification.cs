using Bsft.Mediator;

namespace Bsft.Sample.Orders.Core;

/// <summary>
/// Published after an order is created. The host enables ForwardNotifications() and registers the
/// Bsft.Mediator.EventAggregator bridge, so after the local INotificationHandlers have run this
/// notification is republished through the EventAggregator, where the saga engine picks it up and
/// starts the order fulfillment flow.
/// </summary>
/// <param name="OrderId">The unique identifier of the created order.</param>
/// <param name="ProductId">The product being ordered.</param>
/// <param name="ProductName">The product name at the time of ordering.</param>
/// <param name="Quantity">The quantity ordered.</param>
public record OrderCreatedNotification(
    Guid OrderId,
    Guid ProductId,
    string ProductName,
    int Quantity) : INotification;
