using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapperNullGuardTests
{
    [Fact]
    public void Constructor_NullProfiles_ThrowsArgumentNullException()
    {
        // Mapper.ctor: null profiles guard.
        var act = () => new Mapper(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("profiles");
    }

    [Fact]
    public void MapUntyped_NullSource_ThrowsArgumentNullException()
    {
        // Mapper.Map<TDest>: null source guard.
        var mapper = new Mapper([new ConventionProfile()]);

        var act = () => mapper.Map<DestinationDto>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("source");
    }

    [Fact]
    public void MapTyped_NullSource_ThrowsArgumentNullException()
    {
        // Mapper.Map<TSource,TDest>: null source guard.
        var mapper = new Mapper([new ConventionProfile()]);

        var act = () => mapper.Map<SourceEntity, DestinationDto>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("source");
    }

    [Fact]
    public void MapIntoExisting_NullSource_ThrowsArgumentNullException()
    {
        // Exercises Mapper.Map<TSource,TDest>(source, existing) : null source guard.
        var mapper = new Mapper([new ConventionProfile()]);
        var existing = new DestinationDto();

        var act = () => mapper.Map<SourceEntity, DestinationDto>(null!, existing);

        act.Should().Throw<ArgumentNullException>().WithParameterName("source");
    }

    [Fact]
    public void MapIntoExisting_NullExisting_ThrowsArgumentNullException()
    {
        // Exercises Mapper.Map<TSource,TDest>(source, existing) : null existing guard.
        var mapper = new Mapper([new ConventionProfile()]);
        var source = new SourceEntity { Id = 1 };

        var act = () => mapper.Map<SourceEntity, DestinationDto>(source, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("existing");
    }

    [Fact]
    public void Map_NoRegisteredMapping_ThrowsInvalidOperationException()
    {
        // MappingRegistry.Get: no mapping registered for the pair -> throws.
        var mapper = new Mapper([new ConventionProfile()]);

        var act = () => mapper.Map<SourceEntity, DestinationWithIgnored>(new SourceEntity());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No mapping configured*");
    }
}
