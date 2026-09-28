namespace Bsft.Mapping.Tests;

public class ExplicitProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationWithExtra>()
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName + " " + s.LastName))
            .Ignore<string>(d => d.Unmapped);
    }
}
