using System.Linq.Expressions;
using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapperBuildMemberBindingsTests
{
    [Fact]
    public void GetProjection_CustomMappingOntoAComputedMember_LeavesItOutOfTheProjection()
    {
        // Expression.Bind refuses a member the destination computes itself, so a projection that
        // bound everything a profile named would fail to build at all.
        var mapper = new Mapper([new ComputedMemberProfile()]);

        var projection = mapper.GetProjection<SourceEntity, DestinationWithComputedMember>();

        var result = projection.Compile()(new SourceEntity { Id = 1, FirstName = "Test", LastName = "Case" });
        result.Id.Should().Be(1);
        result.FirstName.Should().Be("Test");
        result.Initials.Should().Be("T");
    }

    [Fact]
    public void GetProjection_AskedTwice_ReturnsTheSameExpression()
    {
        // The tree is built over two types that cannot change, so building it per call builds the
        // same tree again once per query.
        var mapper = new Mapper([new ConventionProfile()]);

        var first = mapper.GetProjection<SourceEntity, DestinationDto>();
        var second = mapper.GetProjection<SourceEntity, DestinationDto>();

        second.Should().BeSameAs(first);
    }
}
