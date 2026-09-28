using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Mapping;
using Bsft.Mediator;
using Bsft.Sample.Orders.Core;

namespace Bsft.Sample.Orders;

/// <summary>
/// Executes a paged query against the orders table, applies filtering and sorting
/// via the query executor, and projects the results to <see cref="OrderDto"/>.
/// </summary>
public class GetOrdersQueryHandler : IQueryHandler<GetOrdersQuery, PagedResult<OrderDto>>
{
    private readonly IOrderDbContext _db;
    private readonly IQueryExecutor<Order, OrderDto> _queryExecutor;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetOrdersQueryHandler"/> class.
    /// </summary>
    public GetOrdersQueryHandler(
        IOrderDbContext db,
        IQueryExecutor<Order, OrderDto> queryExecutor,
        IMapper mapper)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc />
    public async Task<PagedResult<OrderDto>> HandleAsync(GetOrdersQuery query, CancellationToken cancellationToken = default)
    {
        var pagedEntities = await _queryExecutor.ExecuteAsync(
            _db.Orders.AsQueryable(), query.Request, cancellationToken).ConfigureAwait(false);

        var dtos = pagedEntities.Items
            .Select(e => _mapper.Map<Order, OrderDto>(e))
            .ToList();

        return new PagedResult<OrderDto>(
            dtos, pagedEntities.TotalCount, pagedEntities.Page, pagedEntities.PageSize, pagedEntities.HasNextPage);
    }
}
