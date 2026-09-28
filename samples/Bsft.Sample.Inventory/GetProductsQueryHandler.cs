using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Mapping;
using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Executes a paged query against the products table, applies filtering and sorting
/// via the query executor, and projects the results to <see cref="ProductDto"/>.
/// </summary>
public class GetProductsQueryHandler : IQueryHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IInventoryDbContext _db;
    private readonly IQueryExecutor<Product, ProductDto> _queryExecutor;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetProductsQueryHandler"/> class.
    /// </summary>
    public GetProductsQueryHandler(
        IInventoryDbContext db,
        IQueryExecutor<Product, ProductDto> queryExecutor,
        IMapper mapper)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc />
    public async Task<PagedResult<ProductDto>> HandleAsync(GetProductsQuery query, CancellationToken cancellationToken = default)
    {
        var pagedEntities = await _queryExecutor.ExecuteAsync(
            _db.Products.AsQueryable(), query.Request, cancellationToken).ConfigureAwait(false);

        var dtos = pagedEntities.Items
            .Select(e => _mapper.Map<Product, ProductDto>(e))
            .ToList();

        return new PagedResult<ProductDto>(
            dtos, pagedEntities.TotalCount, pagedEntities.Page, pagedEntities.PageSize, pagedEntities.HasNextPage);
    }
}
