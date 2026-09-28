using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;

namespace Bsft.Data.Tests.Support;

/// <summary>
/// A query configuration that names no DefaultSort, so a request carrying no sort of its own
/// reaches the branch that has neither a requested nor a configured sort field to order by.
/// </summary>
internal sealed class NoDefaultSortConfiguration : QueryConfigurationBase<TestEntity, TestDto>
{
    public override void Configure(QueryConfigurationBuilder<TestEntity, TestDto> builder)
    {
        builder.Field(d => d.Name)
            .AllowFilter(FilterOperator.Eq)
            .AllowSort();
        // No DefaultSort() call -> _defaultSortField stays null
    }
}
