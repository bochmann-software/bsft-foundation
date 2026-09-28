using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

/// <summary>Covers how the mapping model is built from the profiles an assembly scan found.</summary>
public class MappingRegistryTests
{
    [Fact]
    public void Mapper_TwoProfilesRegisteringTheSamePair_IsRefusedNamingTheSecond()
    {
        // A profile refuses the pair twice, but two profiles could each register it once and the
        // second would simply replace the first, so which mapping a host got depended on the order
        // the assembly scan happened to return the profiles in.
        var act = () => new Mapper([new ConventionProfile(), new ConventionProfile()]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*more than one profile*")
            .And.Message.Should().Contain(nameof(ConventionProfile));
    }

    [Fact]
    public void Map_AnUnregisteredPair_IsRefusedNamingBothTypes()
    {
        var mapper = new Mapper([new ConventionProfile()]);

        var act = () => mapper.Map<SourceEntity, DestinationWithIgnored>(new SourceEntity());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No mapping configured*");
    }
}
