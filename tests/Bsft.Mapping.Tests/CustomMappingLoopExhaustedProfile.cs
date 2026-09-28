namespace Bsft.Mapping.Tests;

/// <summary>
/// Profile with a custom mapping for Name. When resolving ctor param "Other", convention has no match
/// and the custom mappings loop closes without finding a match.
/// </summary>
public class CustomMappingLoopExhaustedProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceForCustomConvert, DestinationRecordTwoParametersOneUnresolvable>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Name));
    }
}
