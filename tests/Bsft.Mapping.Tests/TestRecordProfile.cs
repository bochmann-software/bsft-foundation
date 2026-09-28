using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class TestRecordProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationRecord>();
    }
}
