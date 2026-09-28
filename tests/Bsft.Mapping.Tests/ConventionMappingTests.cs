using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class ConventionMappingTests
{
    [Fact]
    public void Map_WithMatchingProperties_CopiesAllValues()
    {
        var mapper = new Mapper([new ConventionProfile()]);
        var source = new SourceEntity { Id = 1, FirstName = "Max", LastName = "Mustermann", Email = "max@test.de", Age = 30 };

        var result = mapper.Map<SourceEntity, DestinationDto>(source);

        result.Id.Should().Be(1);
        result.FirstName.Should().Be("Max");
        result.LastName.Should().Be("Mustermann");
        result.Email.Should().Be("max@test.de");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void Map_UntypedOverload_CopiesAllValues()
    {
        var mapper = new Mapper([new ConventionProfile()]);
        object source = new SourceEntity { Id = 5, FirstName = "Anna", LastName = "Schmidt", Email = "anna@test.de", Age = 25 };

        var result = mapper.Map<DestinationDto>(source);

        result.Id.Should().Be(5);
        result.FirstName.Should().Be("Anna");
    }

    [Fact]
    public void Map_IncompatiblePropertyTypes_SkipsProperty()
    {
        var profile = new IncompatibleTypeProfile();
        var mapper = new Mapper([profile]);
        var source = new SourceWithIncompatibleType { Id = 1, Age = "thirty" };

        var result = mapper.Map<SourceWithIncompatibleType, DestinationWithInt>(source);

        result.Id.Should().Be(1);
        result.Age.Should().Be(0);
    }

    private class IncompatibleTypeProfile : MappingProfileBase
    {
        public override void Configure(ProfileBuilder builder)
        {
            builder.CreateMap<SourceWithIncompatibleType, DestinationWithInt>();
        }
    }
}
