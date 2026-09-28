using Bsft.Data.MultiTenancy;

namespace Bsft.Sample.Orders.Core;

/// <summary>
/// Tenant-scoped order entity. The multi-tenancy interceptor stamps TenantId
/// automatically on SaveChanges for any entity implementing <see cref="ITenantEntity"/>.
/// </summary>
public class Order : ITenantEntity
{
    /// <summary>
    /// Gets or sets the unique identifier of the order.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the product being ordered.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the name of the product at the time of ordering.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the quantity of the product ordered.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the unit price of the product at the time of ordering.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the current status of the order.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// Gets or sets the timestamp when the order was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the tenant identifier that owns this order.
    /// </summary>
    public string? TenantId { get; set; }
}
