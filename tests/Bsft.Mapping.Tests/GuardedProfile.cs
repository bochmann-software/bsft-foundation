namespace Bsft.Mapping.Tests;

/// <summary>Profile mapping onto the guarded destination by convention alone.</summary>
public class GuardedProfile : MappingProfileBase
{
    /// <inheritdoc />
    public override void Configure(ProfileBuilder builder)
        => builder.CreateMap<SourceEntity, DestinationWithGuardedOwner>();
}
