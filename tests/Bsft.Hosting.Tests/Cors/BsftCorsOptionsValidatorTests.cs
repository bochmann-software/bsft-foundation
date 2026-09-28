using Bsft.Hosting.Cors;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.Tests.Cors;

public class BsftCorsOptionsValidatorTests
{
    [Fact]
    public void Validate_WithNoOrigins_Succeeds()
    {
        var validation = new BsftCorsOptionsValidator();

        var result = validation.Validate(name: null, new BsftCorsOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithBlankOrigin_FailsNamingTheIndex()
    {
        var validation = new BsftCorsOptionsValidator();
        var options = new BsftCorsOptions();
        options.AllowOrigins("https://example.com", "  ");

        var result = validation.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Origins[1]");
    }

    [Fact]
    public void Validate_WithNullOptions_Throws()
    {
        var validation = new BsftCorsOptionsValidator();

        var act = () => validation.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
