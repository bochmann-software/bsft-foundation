namespace Bsft.Mapping.Tests;

/// <summary>
/// Profile mapping SourceEntity -> DestRecordWithUnresolvableParam.
/// UnresolvableParam has no source match and no default value.
/// </summary>
public class UnresolvableParameterProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationRecordWithUnresolvableParameter>();
    }
}
