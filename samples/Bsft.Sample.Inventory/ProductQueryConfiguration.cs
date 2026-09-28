using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Sample.Inventory.Core;
using Bsft.Data;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Configures which fields are filterable and sortable for product queries.
/// Only whitelisted fields are accepted; unknown fields are rejected.
/// </summary>
public class ProductQueryConfiguration : QueryConfigurationBase<Product, ProductDto>
{
    /// <summary>
    /// Upper bound for the page size a caller may request; larger values are clamped.
    /// </summary>
    private const int MaxProductPageSize = 50;

    /// <inheritdoc />
    public override void Configure(QueryConfigurationBuilder<Product, ProductDto> builder)
    {
        builder.Field(d => d.Name)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.Present)
            .AllowSort();

        builder.Field(d => d.StockQuantity)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Gt, FilterOperator.Lt, FilterOperator.Gte, FilterOperator.Lte)
            .AllowSort();

        builder.Field(d => d.Price)
            .AllowFilter(FilterOperator.Gte, FilterOperator.Lte)
            .AllowSort();

        builder.DefaultSort(d => d.Name, SortDirection.Asc);
        builder.MaxPageSize(MaxProductPageSize);
    }
}
