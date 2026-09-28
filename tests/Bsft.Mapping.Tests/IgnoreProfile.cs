namespace Bsft.Mapping.Tests;

public class IgnoreProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationWithIgnored>()
            .Ignore<string>(d => d.InternalCode);
    }
}
