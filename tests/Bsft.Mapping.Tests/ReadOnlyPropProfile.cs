namespace Bsft.Mapping.Tests;

/// <summary>
/// Profile to map SourceEntity -> DestWithReadOnlyProp, whose read-only member the mapper has no
/// writer for and therefore leaves alone.
/// </summary>
public class ReadOnlyPropProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationWithReadOnlyProp>();
    }
}
