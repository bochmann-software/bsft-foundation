using Bsft.Mapping;
using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Loads the product from the database and maps it to <see cref="ProductDto"/>.
/// </summary>
public class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly IInventoryDbContext _db;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetProductByIdQueryHandler"/> class.
    /// </summary>
    /// <param name="db">The inventory database context.</param>
    /// <param name="mapper">The convention-based object mapper.</param>
    public GetProductByIdQueryHandler(IInventoryDbContext db, IMapper mapper)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc />
    public async Task<ProductDto?> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.FindAsync(new object[] { query.ProductId }, cancellationToken).ConfigureAwait(false);
        return product is null ? null : _mapper.Map<Product, ProductDto>(product);
    }
}
