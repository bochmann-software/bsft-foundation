namespace Bsft.Mapping.Tests;

/// <summary>Profile naming the computed member explicitly.</summary>
public class ComputedMemberProfile : MappingProfileBase
{
    /// <inheritdoc />
    public override void Configure(ProfileBuilder builder)
        => builder.CreateMap<SourceEntity, DestinationWithComputedMember>()
            .ForMember(d => d.Initials, o => o.MapFrom(s => s.LastName));
}
