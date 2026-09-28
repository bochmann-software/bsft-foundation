using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapperReadOnlyPropertyTests
{
    [Fact]
    public void Map_DestinationWithReadOnlyProperty_SkipsReadOnlyProperty()
    {
        // A destination property without a public setter gets no writer, so the assignment is
        // skipped and the destination keeps what it computed.
        var mapper = new Mapper([new ReadOnlyPropProfile()]);
        var source = new SourceEntity { Id = 42, FirstName = "Max" };

        var result = mapper.Map<SourceEntity, DestinationWithReadOnlyProp>(source);

        result.Id.Should().Be(42);
        // Read-only FirstName property is NOT overwritten by the mapper
        result.FirstName.Should().Be("readonly");
    }
}
