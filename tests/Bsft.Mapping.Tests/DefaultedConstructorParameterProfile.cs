namespace Bsft.Mapping.Tests;

/// <summary>Profile mapping onto the destination with the defaulted constructor parameter.</summary>
public class DefaultedConstructorParameterProfile : MappingProfileBase
{
    /// <inheritdoc />
    public override void Configure(ProfileBuilder builder)
        => builder.CreateMap<SourceEntity, DestinationWithDefaultedConstructorParameter>();
}
