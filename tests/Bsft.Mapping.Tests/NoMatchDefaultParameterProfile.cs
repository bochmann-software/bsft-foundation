namespace Bsft.Mapping.Tests;

/// <summary>Profile mapping SourceEntity -> DestRecordOnlyIdWithDefault.</summary>
public class NoMatchDefaultParameterProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationRecordOnlyIdWithDefault>();
    }
}
