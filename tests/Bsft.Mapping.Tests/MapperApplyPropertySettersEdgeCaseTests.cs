using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapperApplyPropertySettersEdgeCaseTests
{
    [Fact]
    public void MapIntoExisting_CustomMappingOntoAComputedMember_LeavesItToTheDestination()
    {
        var mapper = new Mapper([new ComputedMemberProfile()]);
        var source = new SourceEntity { Id = 5, FirstName = "Test", LastName = "Case" };
        var existing = new DestinationWithComputedMember();

        var result = mapper.Map<SourceEntity, DestinationWithComputedMember>(source, existing);

        result.Id.Should().Be(5);
        result.FirstName.Should().Be("Test");
        result.Initials.Should().Be("T");
    }

    [Fact]
    public void MapIntoExisting_APropertyTheDestinationKeepsToItself_IsNotWrittenThrough()
    {
        // A property counts as public as soon as its getter is, so a private setter arrives at the
        // mapper with CanWrite set and SetValue willing to call it. An entity that made a member
        // unsettable did so to stop exactly this write from an outside DTO.
        var mapper = new Mapper([new GuardedProfile()]);
        var source = new SourceEntity { Id = 7, FirstName = "Mallory" };
        var existing = new DestinationWithGuardedOwner("the original owner");

        var result = mapper.Map<SourceEntity, DestinationWithGuardedOwner>(source, existing);

        result.Id.Should().Be(7);
        result.FirstName.Should().Be("the original owner", "only the destination may call a private setter");
    }
}
