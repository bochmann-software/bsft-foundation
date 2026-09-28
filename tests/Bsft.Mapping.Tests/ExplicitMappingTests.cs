using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class ExplicitMappingTests
{
    [Fact]
    public void Map_WithForMember_AppliesCustomMapping()
    {
        var mapper = new Mapper([new ExplicitProfile()]);
        var source = new SourceEntity { Id = 1, FirstName = "Max", LastName = "Mustermann" };

        var result = mapper.Map<SourceEntity, DestinationWithExtra>(source);

        result.FullName.Should().Be("Max Mustermann");
    }

    [Fact]
    public void Map_WithForMember_StillMapsConventionProperties()
    {
        var mapper = new Mapper([new ExplicitProfile()]);
        var source = new SourceEntity { Id = 42, FirstName = "Anna" };

        var result = mapper.Map<SourceEntity, DestinationWithExtra>(source);

        result.Id.Should().Be(42);
        result.FirstName.Should().Be("Anna");
    }

    [Fact]
    public void Map_WithComputedExpression_EvaluatesCorrectly()
    {
        var profile = new ComputedProfile();
        var mapper = new Mapper([profile]);
        var source = new SourceEntity { Id = 1, FirstName = "Test", LastName = "User", Age = 25 };

        var result = mapper.Map<SourceEntity, ComputedDto>(source);

        result.NameAndAge.Should().Be("Test (25)");
    }

    [Fact]
    public void Map_WithCustomMapping_RepeatedCalls_ReuseCompiledDelegate()
    {
        // The custom mapping expression is compiled once at registration; repeated Map calls must
        // keep producing the correct result using the cached delegate.
        var mapper = new Mapper([new ComputedProfile()]);

        for (var i = 0; i < 5; i++)
        {
            var result = mapper.Map<SourceEntity, ComputedDto>(
                new SourceEntity { FirstName = "User" + i, Age = i });

            result.NameAndAge.Should().Be($"User{i} ({i})");
        }
    }

    private class ComputedDto
    {
        public string NameAndAge { get; set; } = string.Empty;
    }

    private class ComputedProfile : MappingProfileBase
    {
        public override void Configure(ProfileBuilder builder)
        {
            builder.CreateMap<SourceEntity, ComputedDto>()
                .ForMember(d => d.NameAndAge, o => o.MapFrom(s => s.FirstName + " (" + s.Age + ")"));
        }
    }
}
