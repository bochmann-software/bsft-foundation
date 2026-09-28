namespace Bsft.Mapping.Tests;

/// <summary>Profile naming the computed member of a constructed destination.</summary>
public class ConstructorAndComputedMemberProfile : MappingProfileBase
{
    /// <inheritdoc />
    public override void Configure(ProfileBuilder builder)
        => builder.CreateMap<SourceEntity, DestinationWithConstructorAndComputedMember>()
            .ForMember(d => d.Initials, o => o.MapFrom(s => s.LastName));
}
