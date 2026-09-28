using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Sample.Orders.Core;
using Bsft.Data;

namespace Bsft.Sample.Orders;

/// <summary>
/// Configures which fields are filterable and sortable for order queries.
/// Only whitelisted fields are accepted; unknown fields are rejected.
/// </summary>
public class OrderQueryConfiguration : QueryConfigurationBase<Order, OrderDto>
{
    /// <summary>
    /// Upper bound for the page size a caller may request; larger values are clamped.
    /// </summary>
    private const int MaxOrderPageSize = 50;

    /// <inheritdoc />
    public override void Configure(QueryConfigurationBuilder<Order, OrderDto> builder)
    {
        builder.Field(d => d.ProductName)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Contains)
            .AllowSort();

        builder.Field(d => d.Status)
            .AllowFilter(FilterOperator.Eq);

        builder.Field(d => d.Quantity)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Gt, FilterOperator.Lt)
            .AllowSort();

        builder.Field(d => d.UnitPrice)
            .AllowFilter(FilterOperator.Gte, FilterOperator.Lte)
            .AllowSort();

        // Declared because the default sort below names it. A DefaultSort on a field the
        // configuration never declares sortable is refused by QueryConfigurationStartupValidator.
        builder.Field(d => d.CreatedAt)
            .AllowFilter(FilterOperator.Gte, FilterOperator.Lte)
            .AllowSort();

        builder.DefaultSort(d => d.CreatedAt, SortDirection.Desc);
        builder.MaxPageSize(MaxOrderPageSize);
    }
}
