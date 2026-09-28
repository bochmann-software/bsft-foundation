using Bsft.Mapping;
using Bsft.Mediator;
using Bsft.Sample.Orders.Core;

namespace Bsft.Sample.Orders;

/// <summary>
/// Loads the order from the database and maps it to <see cref="OrderDto"/>.
/// </summary>
public class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly IOrderDbContext _db;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetOrderByIdQueryHandler"/> class.
    /// </summary>
    /// <param name="db">The order database context.</param>
    /// <param name="mapper">The convention-based object mapper.</param>
    public GetOrderByIdQueryHandler(IOrderDbContext db, IMapper mapper)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc />
    public async Task<OrderDto?> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.FindAsync(new object[] { query.OrderId }, cancellationToken).ConfigureAwait(false);
        return order is null ? null : _mapper.Map<Order, OrderDto>(order);
    }
}
