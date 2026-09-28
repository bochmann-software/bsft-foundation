namespace Bsft.Mapping.Tests;

/// <summary>Profile mapping SourceWithStringName -> DestRecordWithObjectName (string -> object conversion).</summary>
public class StringToObjectProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceWithStringName, DestinationRecordWithObjectName>();
    }
}
