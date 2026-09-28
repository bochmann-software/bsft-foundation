using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Query to retrieve a single product by its identifier.
/// </summary>
/// <param name="ProductId">The identifier of the product to retrieve.</param>
public record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto?>;
