using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapperCreateAndPopulateEdgeCaseTests
{
    [Fact]
    public void Map_RecordWithUnresolvableCtorParam_ThrowsInvalidOperationException()
    {
        // DestRecordWithUnresolvableParam takes an UnresolvableParam with no convention match and
        // no default value, so the only constructor fails to resolve and none is left to try.
        var profile = new UnresolvableParameterProfile();
        var mapper = new Mapper([profile]);
        var source = new SourceEntity { Id = 1, FirstName = "Alice" };

        // Should throw because no ctor can be fully resolved
        var act = () => mapper.Map<SourceEntity, DestinationRecordWithUnresolvableParameter>(source);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot create an instance*");
    }

    [Fact]
    public void Map_DestWithReadOnlyNonCtorConventionProp_LeavesWhatItCannotAssign()
    {
        // After constructing through the ctor, a convention-matched property that the ctor did not
        // take and that the destination does not let the mapper assign is left as the ctor left it.
        var profile = new ConstructorAndReadOnlyConventionPropProfile();
        var mapper = new Mapper([profile]);
        var source = new SourceEntity { Id = 99, FirstName = "Leo", LastName = "King" };

        var result = mapper.Map<SourceEntity, DestinationWithConstructorAndReadOnlyConventionProp>(source);

        result.FirstName.Should().Be("Leo");
        result.Id.Should().Be(99);
        // LastName is read-only and was skipped; it returns its computed value
        result.LastName.Should().Be("readonly");
    }

    [Fact]
    public void Map_CtorDestWithACustomMappingOntoAComputedMember_LeavesItToTheDestination()
    {
        // A destination may compute a member itself, and a profile may still name it. There is no
        // setter to call, so the constructed instance keeps what it computes.
        var mapper = new Mapper([new ConstructorAndComputedMemberProfile()]);
        var source = new SourceEntity { Id = 3, FirstName = "Bob", LastName = "Smith" };

        var result = mapper.Map<SourceEntity, DestinationWithConstructorAndComputedMember>(source);

        result.FirstName.Should().Be("Bob");
        result.Id.Should().Be(3);
        result.Initials.Should().Be("B");
    }
}
