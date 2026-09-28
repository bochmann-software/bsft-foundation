using Bsft.Core;
using Bsft.Data.Contracts.Pagination;
using Bsft.Mediator;
using Bsft.Sample.Orders.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bsft.Sample.Orders;

/// <summary>
/// Orders API. Demonstrates CQRS commands and queries dispatched through the mediator.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersController"/> class.
    /// </summary>
    /// <param name="mediator">The mediator for dispatching commands and queries.</param>
    public OrdersController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// Creates a new order and triggers the order fulfillment saga.
    /// </summary>
    /// <param name="request">The order creation request body.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The identifier of the newly created order.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateOrderCommand(
            request.ProductId,
            request.Quantity);

        var id = await _mediator.SendAsync<Guid>(command, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>
    /// Gets a paged, filtered, and sorted list of orders.
    /// </summary>
    /// <param name="request">The pagination, filter, and sort parameters.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A paged result of order DTOs.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.QueryAsync(new GetOrdersQuery(request), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Gets an order by its identifier.
    /// </summary>
    /// <param name="id">The order identifier.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The order details.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _mediator.QueryAsync(new GetOrderByIdQuery(id), cancellationToken).ConfigureAwait(false);
        if (order is null)
            throw new NotFoundException("Order", id);

        return Ok(order);
    }
}
