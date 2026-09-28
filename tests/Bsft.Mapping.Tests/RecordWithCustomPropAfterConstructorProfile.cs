namespace Bsft.Mapping.Tests;

/// <summary>Profile for DestMixedWithCustomProp.</summary>
public class RecordWithCustomPropAfterConstructorProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationMixedWithCustomProp>()
            .ForMember(d => d.FullNameProp, o => o.MapFrom(s => s.FirstName + " " + s.LastName));
    }
}
