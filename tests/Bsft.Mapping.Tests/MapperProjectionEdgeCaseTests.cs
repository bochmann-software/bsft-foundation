using System.Linq.Expressions;
using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapperProjectionEdgeCaseTests
{
    [Fact]
    public void GetProjection_RecordWithDefaultParamNotInSource_UsesConstantDefault()
    {
        // MapperProjections.BuildViaConstructor:
        // A record ctor param that has no source match but has a default value
        // -> uses Expression.Constant(defaultValue, paramType).
        var profile = new NoMatchDefaultParameterProfile();
        var mapper = new Mapper([profile]);

        var projection = mapper.GetProjection<SourceEntity, DestinationRecordOnlyIdWithDefault>();
        var compiled = projection.Compile();
        var source = new SourceEntity { Id = 10 };

        var result = compiled(source);

        result.Id.Should().Be(10);
        result.ExtraWithDefault.Should().Be("fallback");
    }

    [Fact]
    public void GetProjection_RecordWithUnresolvableParamAndNoDefault_ThrowsInvalidOperationException()
    {
        // MapperProjections.BuildViaConstructor: no ctor resolves every parameter -> throws.
        // DestRecordWithUnresolvableParam has 'UnresolvableParam' with no match and no default.
        var profile = new UnresolvableParameterProfile();
        var mapper = new Mapper([profile]);

        var act = () => mapper.GetProjection<SourceEntity, DestinationRecordWithUnresolvableParameter>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot build projection*");
    }

    [Fact]
    public void GetProjection_RecordWithAdditionalPropertyBindings_IncludesMemberInit()
    {
        // MapperProjections.BuildViaConstructor:
        // DestMixedCtorAndProp has ctor (FirstName, LastName) + a remaining property Id.
        // The additional binding (Id) triggers the memberInit path.
        var mapper = new Mapper([new MixedConstructorAndPropProfile()]);

        var projection = mapper.GetProjection<SourceEntity, DestinationMixedConstructorAndProp>();
        var compiled = projection.Compile();
        var source = new SourceEntity { Id = 99, FirstName = "Zoe", LastName = "Jones" };

        var result = compiled(source);

        result.FirstName.Should().Be("Zoe");
        result.LastName.Should().Be("Jones");
        result.Id.Should().Be(99);
    }

    [Fact]
    public void GetProjection_CustomMappingOnCtorParam_UsesTheCustomExpression()
    {
        // MapperProjections.ResolveParameterExpression:
        // custom mapping lookup path when a ctor param name matches a custom mapping.
        var profile = new TestRecordWithExtraProfile();
        var mapper = new Mapper([profile]);

        // DestRecordWithExtra(int Id, string FirstName, string FullName)
        // FullName is a custom mapping -> resolved through the custom-mapping lookup.
        var projection = mapper.GetProjection<SourceEntity, DestinationRecordWithExtra>();
        var compiled = projection.Compile();
        var source = new SourceEntity { Id = 1, FirstName = "Leo", LastName = "King" };

        var result = compiled(source);

        result.FullName.Should().Be("Leo King");
    }

    [Fact]
    public void GetProjection_ConventionParamWithTypeConversion_ConvertsToTheTargetType()
    {
        // MapperProjections.ResolveParameterExpression: access.Type != targetType -> Convert.
        // SourceWithStringName.Name is 'string'; DestRecordWithObjectName(object Name) ctor param is 'object'.
        // Convention mapping passes (object.IsAssignableFrom(string)=true).
        // In the expression tree: Expression.Property gives type 'string', targetType is 'object' -> Convert.
        var profile = new StringToObjectProfile();
        var mapper = new Mapper([profile]);

        var projection = mapper.GetProjection<SourceWithStringName, DestinationRecordWithObjectName>();
        var compiled = projection.Compile();
        var source = new SourceWithStringName { Name = "hello" };

        var result = compiled(source);

        result.Name.Should().Be("hello");
    }
}
