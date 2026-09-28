namespace Bsft.Mapping.Tests;

/// <summary>Profile mapping SourceEntity -> DestWithCtorAndReadOnlyConventionProp.</summary>
public class ConstructorAndReadOnlyConventionPropProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<SourceEntity, DestinationWithConstructorAndReadOnlyConventionProp>();
    }
}
