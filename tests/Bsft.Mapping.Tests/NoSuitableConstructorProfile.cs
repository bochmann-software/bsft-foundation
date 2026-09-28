namespace Bsft.Mapping.Tests;

/// <summary>Profile mapping SourceEntity -> DestWithNoSuitableCtor.</summary>
public class NoSuitableConstructorProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationWithNoSuitableConstructor>();
    }
}
