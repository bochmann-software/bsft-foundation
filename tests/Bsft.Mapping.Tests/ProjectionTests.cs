using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class ProjectionTests
{
    [Fact]
    public void GetProjection_ADestinationWithAComputedConventionMember_LeavesItOutOfTheBindings()
    {
        // The member matches by name and type but has no setter, so binding it would fail the
        // projection's construction where the in-memory map simply skips it.
        var mapper = new Mapper([new ConstructorAndReadOnlyConventionPropProfile()]);

        var projection = mapper.GetProjection<SourceEntity, DestinationWithConstructorAndReadOnlyConventionProp>();

        var result = projection.Compile()(new SourceEntity { Id = 4, FirstName = "Leo", LastName = "King" });
        result.FirstName.Should().Be("Leo");
        result.Id.Should().Be(4);
        result.LastName.Should().Be("readonly");
    }

    [Fact]
    public void GetProjection_ADefaultedParameterWithNoPropertyOfItsName_StillBuilds()
    {
        // The members overload lets a query provider read an argument back as the property it
        // fills, and a parameter that has no such property would make it refuse the construction
        // outright rather than lose the naming.
        var mapper = new Mapper([new DefaultedConstructorParameterProfile()]);

        var projection = mapper.GetProjection<SourceEntity, DestinationWithDefaultedConstructorParameter>();

        var result = projection.Compile()(new SourceEntity { Id = 8, FirstName = "Ada" });
        result.Id.Should().Be(8);
        result.Divider.Should().Be("-");
    }

    [Fact]
    public void GetProjection_ReturnsValidExpression()
    {
        var mapper = new Mapper([new ConventionProfile()]);

        var projection = mapper.GetProjection<SourceEntity, DestinationDto>();

        projection.Should().NotBeNull();
        projection.Body.Should().NotBeNull();
    }

    [Fact]
    public void GetProjection_CompiledExpression_ProducesCorrectResult()
    {
        var mapper = new Mapper([new ConventionProfile()]);
        var projection = mapper.GetProjection<SourceEntity, DestinationDto>();
        var compiled = projection.Compile();
        var source = new SourceEntity { Id = 10, FirstName = "Max", LastName = "Mustermann", Email = "max@test.de", Age = 30 };

        var result = compiled(source);

        result.Id.Should().Be(10);
        result.FirstName.Should().Be("Max");
        result.LastName.Should().Be("Mustermann");
        result.Email.Should().Be("max@test.de");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void GetProjection_WithCustomMapping_IncludesCustomBindings()
    {
        var mapper = new Mapper([new ExplicitProfile()]);
        var projection = mapper.GetProjection<SourceEntity, DestinationWithExtra>();
        var compiled = projection.Compile();
        var source = new SourceEntity { Id = 1, FirstName = "Anna", LastName = "Schmidt" };

        var result = compiled(source);

        result.FullName.Should().Be("Anna Schmidt");
        result.Id.Should().Be(1);
    }

    [Fact]
    public void ProjectTo_WithQueryable_ProjectsCorrectly()
    {
        var mapper = new Mapper([new ConventionProfile()]);
        var sources = new List<SourceEntity>
        {
            new() { Id = 1, FirstName = "Max", LastName = "Mustermann" },
            new() { Id = 2, FirstName = "Anna", LastName = "Schmidt" }
        };

        var results = sources.AsQueryable().ProjectTo<DestinationDto>(mapper).ToList();

        results.Should().HaveCount(2);
        results[0].FirstName.Should().Be("Max");
        results[1].FirstName.Should().Be("Anna");
    }

    [Fact]
    public void ProjectTo_NullSource_ThrowsArgumentNullException()
    {
        // ProjectToExtensions.ProjectTo: null source guard.
        var mapper = new Mapper([new ConventionProfile()]);
        IQueryable source = null!;

        var act = () => source.ProjectTo<DestinationDto>(mapper);

        act.Should().Throw<ArgumentNullException>().WithParameterName("source");
    }

    [Fact]
    public void ProjectTo_NullMapper_ThrowsArgumentNullException()
    {
        // ProjectToExtensions.ProjectTo: null mapper guard.
        var sources = new List<SourceEntity>().AsQueryable();

        var act = () => sources.ProjectTo<DestinationDto>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("mapper");
    }
}
