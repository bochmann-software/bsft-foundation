using Bsft.Data.Contracts.Pagination;
using Bsft.Mediator;
using Bsft.Sample.Orders.Core;

namespace Bsft.Sample.Orders;

/// <summary>
/// Query for retrieving a paged, filtered, and sorted list of orders.
/// </summary>
/// <param name="Request">The pagination, filter, and sort parameters.</param>
public record GetOrdersQuery(PagedRequest Request) : IQuery<PagedResult<OrderDto>>;
