using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;

namespace Bsft.Data.Tests.Support;

public sealed class CandidateQueryConfiguration : QueryConfigurationBase<Candidate, CandidateDto>
{
    public override void Configure(QueryConfigurationBuilder<Candidate, CandidateDto> builder)
    {
        builder.Field(d => d.Id).AllowSort();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq).AllowSort();
        builder.DefaultSort(d => d.Id, SortDirection.Asc);

        builder.Searchable("profile", s => s
            .Field(e => e.Name, weight: 2.0)
            .Field(e => e.Notes)
            .Language("de")
            .Mode(SearchMode.FreeText));
    }
}
