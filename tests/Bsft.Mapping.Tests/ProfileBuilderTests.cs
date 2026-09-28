using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class ProfileBuilderTests
{
    [Fact]
    public void CreateMap_DuplicateMapping_ThrowsInvalidOperationException()
    {
        // ProfileBuilder.CreateMap:
        // calling CreateMap<TSource, TDest> twice for the same pair throws.
        var builder = new ProfileBuilder();
        builder.CreateMap<string, int>();

        var act = () => builder.CreateMap<string, int>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been registered*");
    }
}
