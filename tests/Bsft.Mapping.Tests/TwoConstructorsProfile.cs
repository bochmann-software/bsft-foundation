namespace Bsft.Mapping.Tests;

/// <summary>Profile mapping onto the destination with two public constructors.</summary>
public class TwoConstructorsProfile : MappingProfileBase
{
    /// <inheritdoc />
    public override void Configure(ProfileBuilder builder)
        => builder.CreateMap<SourceEntity, DestinationWithTwoConstructors>();
}
