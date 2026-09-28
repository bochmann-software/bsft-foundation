using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class NestedMappingTests
{
    [Fact]
    public void Map_WithNestedNavigation_MapsCorrectly()
    {
        var mapper = new Mapper([new NestedProfile()]);
        var source = new OrderEntity
        {
            Id = 42,
            Customer = new CustomerEntity { LastName = "Mustermann", City = "Berlin" }
        };

        var result = mapper.Map<OrderEntity, OrderDto>(source);

        result.Id.Should().Be(42);
        result.CustomerLastName.Should().Be("Mustermann");
        result.CustomerCity.Should().Be("Berlin");
    }

    [Fact]
    public void GetProjection_WithNestedNavigation_ProducesCorrectExpression()
    {
        var mapper = new Mapper([new NestedProfile()]);
        var projection = mapper.GetProjection<OrderEntity, OrderDto>();
        var compiled = projection.Compile();
        var source = new OrderEntity
        {
            Id = 10,
            Customer = new CustomerEntity { LastName = "Schmidt", City = "Hamburg" }
        };

        var result = compiled(source);

        result.Id.Should().Be(10);
        result.CustomerLastName.Should().Be("Schmidt");
        result.CustomerCity.Should().Be("Hamburg");
    }
}
