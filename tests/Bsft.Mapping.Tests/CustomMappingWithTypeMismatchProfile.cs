namespace Bsft.Mapping.Tests;

/// <summary>Profile that explicitly maps SourceForCustomConvert.Name -> DestRecordObjParamCustomMapped.Name.</summary>
public class CustomMappingWithTypeMismatchProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceForCustomConvert, DestinationRecordObjParameterCustomMapped>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Name));
    }
}
