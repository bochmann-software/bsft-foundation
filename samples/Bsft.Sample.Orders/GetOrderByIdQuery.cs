using Bsft.Mediator;
using Bsft.Sample.Orders.Core;

namespace Bsft.Sample.Orders;

/// <summary>
/// Query to retrieve a single order by its identifier.
/// </summary>
/// <param name="OrderId">The identifier of the order to retrieve.</param>
public record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto?>;
