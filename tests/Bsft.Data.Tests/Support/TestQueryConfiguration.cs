using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;

namespace Bsft.Data.Tests.Support;

public sealed class TestQueryConfiguration : QueryConfigurationBase<TestEntity, TestDto>
{
    public override void Configure(QueryConfigurationBuilder<TestEntity, TestDto> builder)
    {
        builder.Field(d => d.Name)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Contains, FilterOperator.StartsWith)
            .AllowSort();

        builder.Field(d => d.Age)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Gt, FilterOperator.Lt, FilterOperator.Gte, FilterOperator.Lte)
            .AllowSort();

        builder.Field(d => d.Email)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Contains)
            .AllowSort();

        builder.Field(d => d.Price)
            .AllowFilter(FilterOperator.Gt, FilterOperator.Lt, FilterOperator.Gte, FilterOperator.Lte)
            .AllowSort();

        builder.MaxPageSize(50);
        builder.DefaultSort(d => d.Name, SortDirection.Asc);
    }
}
