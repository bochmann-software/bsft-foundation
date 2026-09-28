namespace Bsft.Sample.Orders.Core;

/// <summary>
/// Read-only DTO returned by order queries. Mapped from <see cref="Order"/> via convention mapping.
/// </summary>
public record OrderDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    OrderStatus Status,
    DateTime CreatedAt);
