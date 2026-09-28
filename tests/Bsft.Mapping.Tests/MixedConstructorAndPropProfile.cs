namespace Bsft.Mapping.Tests;

/// <summary>
/// Profile to map SourceEntity -> DestMixedCtorAndProp.
/// FirstName and LastName go via ctor; Id goes via property setter afterward.
/// </summary>
public class MixedConstructorAndPropProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationMixedConstructorAndProp>();
    }
}
