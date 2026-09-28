using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class TestRecordWithExtraProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationRecordWithExtra>()
            .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.FirstName + " " + s.LastName));
    }
}
