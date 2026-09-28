using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class IgnoreTests
{
    [Fact]
    public void Map_IgnoredProperty_RemainsDefault()
    {
        var mapper = new Mapper([new IgnoreProfile()]);
        var source = new SourceEntity { Id = 1, FirstName = "Max" };

        var result = mapper.Map<SourceEntity, DestinationWithIgnored>(source);

        result.InternalCode.Should().Be(string.Empty);
    }

    [Fact]
    public void Map_IgnoredProperty_ConventionPropertiesStillMapped()
    {
        var mapper = new Mapper([new IgnoreProfile()]);
        var source = new SourceEntity { Id = 7, FirstName = "Anna" };

        var result = mapper.Map<SourceEntity, DestinationWithIgnored>(source);

        result.Id.Should().Be(7);
        result.FirstName.Should().Be("Anna");
    }

    [Fact]
    public void Validation_IgnoredProperty_DoesNotThrow()
    {
        var mapper = new Mapper([new IgnoreProfile()]);
        var service = new MappingValidator(mapper);

        var act = () => service.Validate();

        act.Should().NotThrow();
    }
}
