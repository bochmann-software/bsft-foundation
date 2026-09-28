using Bsft.Hosting.Filtering;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.Tests.Filtering;

public class BsftFilterTransportOptionsValidatorTests
{
    // The registration validates eagerly, so the failure arm of this validator is only reached when a
    // configuration source reloads a bound section into an instance the registration never saw. That is
    // exactly what the validator exists for, and it is why it is exercised here directly.
    [Fact]
    public void Validate_WithUsableBounds_Succeeds()
    {
        var validation = new BsftFilterTransportOptionsValidator();

        var result = validation.Validate(name: null, new BsftFilterTransportOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithReaderDepthBelowAstDepth_FailsNamingTheMember()
    {
        var validation = new BsftFilterTransportOptionsValidator();
        var options = new BsftFilterTransportOptions { MaxJsonReaderDepth = 2, MaxDepth = 24 };

        var result = validation.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftFilterTransportOptions.MaxJsonReaderDepth));
    }

    [Fact]
    public void Validate_WithNullOptions_Throws()
    {
        var validation = new BsftFilterTransportOptionsValidator();

        var act = () => validation.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
