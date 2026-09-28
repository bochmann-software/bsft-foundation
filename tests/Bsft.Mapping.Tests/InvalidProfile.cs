namespace Bsft.Mapping.Tests;

public class InvalidProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationIncomplete>();
    }
}
