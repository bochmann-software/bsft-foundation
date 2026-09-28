namespace Bsft.Sample.Orders.Core;

/// <summary>
/// Represents the lifecycle status of an order.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order has been created but not yet processed.</summary>
    Pending,

    /// <summary>Stock has been reserved and the order is confirmed.</summary>
    Confirmed,

    /// <summary>The order was cancelled due to insufficient stock or other reasons.</summary>
    Canceled
}
