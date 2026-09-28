using Bsft.Data.Contracts.Pagination;
using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Query for retrieving a paged, filtered, and sorted list of products.
/// </summary>
/// <param name="Request">The pagination, filter, and sort parameters.</param>
public record GetProductsQuery(PagedRequest Request) : IQuery<PagedResult<ProductDto>>;
