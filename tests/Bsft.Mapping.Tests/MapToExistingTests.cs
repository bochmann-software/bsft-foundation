using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapToExistingTests
{
    [Fact]
    public void Map_IntoExistingObject_UpdatesProperties()
    {
        var mapper = new Mapper([new ConventionProfile()]);
        var source = new SourceEntity { Id = 1, FirstName = "Max", LastName = "Mustermann", Email = "max@test.de", Age = 30 };
        var existing = new DestinationDto { Id = 99, FirstName = "Old", LastName = "Value", Email = "old@test.de", Age = 0 };

        var result = mapper.Map(source, existing);

        result.Should().BeSameAs(existing);
        result.Id.Should().Be(1);
        result.FirstName.Should().Be("Max");
        result.LastName.Should().Be("Mustermann");
        result.Email.Should().Be("max@test.de");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void Map_IntoExistingObject_WithCustomMapping_AppliesOverrides()
    {
        var mapper = new Mapper([new ExplicitProfile()]);
        var source = new SourceEntity { Id = 5, FirstName = "Anna", LastName = "Schmidt" };
        var existing = new DestinationWithExtra { Id = 0, FirstName = "Old", FullName = "Old Name" };

        var result = mapper.Map(source, existing);

        result.Should().BeSameAs(existing);
        result.Id.Should().Be(5);
        result.FirstName.Should().Be("Anna");
        result.FullName.Should().Be("Anna Schmidt");
    }
}
