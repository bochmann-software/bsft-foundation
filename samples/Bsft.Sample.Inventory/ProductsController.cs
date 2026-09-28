using Bsft.Core;
using Bsft.Data.Contracts.Pagination;
using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Products API. The reference implementation of the framework-wide CRUD convention documented in
/// <c>docs/crud-conventions.md</c>: list, read, create, update and delete on one resource, with
/// every failure answered by the exception the middleware maps to the documented status code.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductsController"/> class.
    /// </summary>
    /// <param name="mediator">The mediator for dispatching commands and queries.</param>
    public ProductsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// Creates a new product in the inventory.
    /// </summary>
    /// <param name="request">The product creation request body.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The identifier of the newly created product.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateProductCommand(request.Name, request.StockQuantity, request.Price);
        var id = await _mediator.SendAsync<Guid>(command, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>
    /// Gets a paged, filtered, and sorted list of products.
    /// </summary>
    /// <remarks>
    /// The filter binds from the <c>filter</c> query parameter per the filter-transport section of
    /// <c>docs/crud-conventions.md</c>; a malformed, oversized or non-whitelisted filter is a <c>400</c>.
    /// </remarks>
    /// <param name="request">The pagination, filter, and sort parameters.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A paged result of product DTOs, each carrying the <c>etag</c> <see cref="GetById"/> serves for it.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.QueryAsync(new GetProductsQuery(request), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Gets a product by its identifier.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The product details, with the product's version as a strong <c>ETag</c>.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await _mediator.QueryAsync(new GetProductByIdQuery(id), cancellationToken).ConfigureAwait(false);
        if (product is null)
            throw new NotFoundException("Product", id);

        // The tag travels back in the header as well as in the body, which is the value the caller
        // echoes as If-Match on its next write — the same value the list already gave it.
        SetEntityTag(product);
        return Ok(product);
    }

    /// <summary>Replaces the editable state of a product; requires an <c>If-Match</c> naming the version being edited.</summary>
    /// <param name="id">The identifier of the product to update.</param>
    /// <param name="request">The new product state.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The product as it stands after the update, with its new version as a strong <c>ETag</c>.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateProductCommand(id, request.Name, request.StockQuantity, request.Price, RequireIfMatch());
        var product = await _mediator.SendAsync<ProductDto>(command, cancellationToken).ConfigureAwait(false);
        SetEntityTag(product);
        return Ok(product);
    }

    /// <summary>
    /// Deletes a product. Requires an <c>If-Match</c> naming the version being deleted.
    /// </summary>
    /// <param name="id">The identifier of the product to delete.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>No content when the product was deleted.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var ifMatch = RequireIfMatch();
        await _mediator.SendAsync(new DeleteProductCommand(id, ifMatch), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Reads the <c>If-Match</c> a conditional write must carry, refusing the write when it is absent.
    /// </summary>
    /// <returns>The raw <c>If-Match</c> header value.</returns>
    /// <exception cref="PreconditionRequiredException">
    /// Thrown when no <c>If-Match</c> was sent; the middleware maps it to <c>428 Precondition Required</c>,
    /// which tells the caller to read the resource for its current version and try again.
    /// </exception>
    private string RequireIfMatch()
    {
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
            throw new PreconditionRequiredException(
                "This operation requires an If-Match header carrying the version of the product being edited.");

        return ifMatch;
    }

    /// <summary>
    /// Writes the product's entity tag as the strong <c>ETag</c> of the response, the value a client
    /// echoes as <c>If-Match</c> on its next conditional write.
    /// </summary>
    /// <remarks>
    /// The header is the tag the DTO already carries rather than a second derivation from the
    /// version, so the value this response advertises is byte-identical to the one the same product
    /// carries as a row of the list — and to the one a conditional write is checked against, since
    /// all three come from the shared <c>Bsft.Http</c> primitive called once in the mapping profile.
    /// </remarks>
    /// <param name="product">The product whose tag to publish.</param>
    private void SetEntityTag(ProductDto product) =>
        Response.Headers.ETag = product.ETag;
}
