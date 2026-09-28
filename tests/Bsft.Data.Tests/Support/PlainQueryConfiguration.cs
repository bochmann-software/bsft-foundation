using Bsft.Data.Filtering;

namespace Bsft.Data.Tests.Support;

/// <summary>A configuration without any searchable declaration.</summary>
public sealed class PlainQueryConfiguration : QueryConfigurationBase<Candidate, CandidateDto>
{
    public override void Configure(QueryConfigurationBuilder<Candidate, CandidateDto> builder) =>
        builder.Field(d => d.Name).AllowSort();
}
