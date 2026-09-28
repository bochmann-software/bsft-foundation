namespace Bsft.Mapping.Tests;

public class ConventionProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationDto>();
    }
}
