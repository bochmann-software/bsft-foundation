using System.Linq.Expressions;
using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapperResolveParameterTests
{
    [Fact]
    public void GetProjection_CustomMappingWithTypeMismatch_InsertsConvert()
    {
        // MapperProjections.ResolveParameterExpression:
        // When a custom mapping expression's type (string) != the ctor param type (object),
        // Expression.Convert is inserted.
        // SourceForCustomConvert.Name is string; DestRecordObjParamCustomMapped(object Name).
        var mapper = new Mapper([new CustomMappingWithTypeMismatchProfile()]);

        var projection = mapper.GetProjection<SourceForCustomConvert, DestinationRecordObjParameterCustomMapped>();
        var compiled = projection.Compile();
        var result = compiled(new SourceForCustomConvert { Name = "hello" });

        result.Name.Should().Be("hello");
    }

    [Fact]
    public void GetProjection_CustomMappingLoopExhaustedForUnresolvableParam_ThrowsInvalidOperation()
    {
        // MapperProjections.ResolveParameterExpression:
        // Neither a convention match nor a custom mapping names the parameter "Other", so the
        // parameter does not resolve, no constructor is left to try, and the build throws.
        var mapper = new Mapper([new CustomMappingLoopExhaustedProfile()]);

        var act = () => mapper.GetProjection<SourceForCustomConvert, DestinationRecordTwoParametersOneUnresolvable>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot build projection*");
    }
}
