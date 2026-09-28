using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MemberMapOptionsTests
{
    [Fact]
    public void MapFrom_NullExpression_ThrowsArgumentNullException()
    {
        // MemberMapOptions<,>.MapFrom: null sourceExpr guard.
        var options = new MemberMapOptions<string, int>();
        System.Linq.Expressions.Expression<Func<string, int>> nullExpr = null!;

        var act = () => options.MapFrom(nullExpr);

        act.Should().Throw<ArgumentNullException>().WithParameterName("sourceExpression");
    }
}
