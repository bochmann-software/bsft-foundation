using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class RecordMappingTests
{
    [Fact]
    public void Map_ToRecord_UsesConstructorParameters()
    {
        var profile = new TestRecordProfile();
        var mapper = new Mapper(new[] { profile });

        var source = new SourceEntity { Id = 1, FirstName = "John", LastName = "Doe", Email = "j@d.com", Age = 30 };
        var result = mapper.Map<SourceEntity, DestinationRecord>(source);

        result.Id.Should().Be(1);
        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.Email.Should().Be("j@d.com");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void Map_ToRecord_WithCustomMapping()
    {
        var profile = new TestRecordWithExtraProfile();
        var mapper = new Mapper(new[] { profile });

        var source = new SourceEntity { Id = 1, FirstName = "John", LastName = "Doe" };
        var result = mapper.Map<SourceEntity, DestinationRecordWithExtra>(source);

        result.Id.Should().Be(1);
        result.FirstName.Should().Be("John");
        result.FullName.Should().Be("John Doe");
    }

    [Fact]
    public void Map_UntypedOverload_ToRecord()
    {
        var profile = new TestRecordProfile();
        var mapper = new Mapper(new[] { profile });

        var source = new SourceEntity { Id = 5, FirstName = "Jane", LastName = "Smith", Email = "j@s.com", Age = 25 };
        var result = mapper.Map<DestinationRecord>(source);

        result.Id.Should().Be(5);
        result.FirstName.Should().Be("Jane");
    }

    [Fact]
    public void GetProjection_ForRecord_ProducesValidExpression()
    {
        var profile = new TestRecordProfile();
        var mapper = new Mapper(new[] { profile });

        var projection = mapper.GetProjection<SourceEntity, DestinationRecord>();

        projection.Should().NotBeNull();
        var compiled = projection.Compile();
        var source = new SourceEntity { Id = 7, FirstName = "Test", LastName = "User", Email = "t@u.com", Age = 42 };
        var result = compiled(source);

        result.Id.Should().Be(7);
        result.FirstName.Should().Be("Test");
        result.Age.Should().Be(42);
    }

    [Fact]
    public void GetProjection_ForRecordWithCustomMapping_ProducesValidExpression()
    {
        var profile = new TestRecordWithExtraProfile();
        var mapper = new Mapper(new[] { profile });

        var projection = mapper.GetProjection<SourceEntity, DestinationRecordWithExtra>();

        projection.Should().NotBeNull();
        var compiled = projection.Compile();
        var source = new SourceEntity { Id = 3, FirstName = "Alice", LastName = "Wonder" };
        var result = compiled(source);

        result.Id.Should().Be(3);
        result.FullName.Should().Be("Alice Wonder");
    }
}
