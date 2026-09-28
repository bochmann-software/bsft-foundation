using Bsft.Hosting.Filtering;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.Tests.Filtering;

public class BsftFilterTransportOptionsTests
{
    [Fact]
    public void Defaults_AreCoherentAndValid()
    {
        var options = new BsftFilterTransportOptions();

        options.MaxFilterLength.Should().Be(BsftFilterTransportOptions.DefaultMaxFilterLength);
        options.MaxJsonReaderDepth.Should().Be(BsftFilterTransportOptions.DefaultMaxJsonReaderDepth);
        options.MaxDepth.Should().Be(BsftFilterTransportOptions.DefaultMaxDepth);
        options.MaxNodeCount.Should().Be(BsftFilterTransportOptions.DefaultMaxNodeCount);

        var act = () => options.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveMaxFilterLength_Throws(int value)
    {
        var options = new BsftFilterTransportOptions { MaxFilterLength = value };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxFilterLength*");
    }

    [Fact]
    public void Validate_NonPositiveMaxJsonReaderDepth_Throws()
    {
        var options = new BsftFilterTransportOptions { MaxJsonReaderDepth = 0 };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxJsonReaderDepth*");
    }

    [Fact]
    public void Validate_NonPositiveMaxDepth_Throws()
    {
        var options = new BsftFilterTransportOptions { MaxDepth = 0 };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxDepth*");
    }

    [Fact]
    public void Validate_NonPositiveMaxNodeCount_Throws()
    {
        var options = new BsftFilterTransportOptions { MaxNodeCount = 0 };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxNodeCount*");
    }

    [Fact]
    public void Validate_ReaderDepthBelowAstDepth_Throws()
    {
        var options = new BsftFilterTransportOptions { MaxDepth = 40, MaxJsonReaderDepth = 20 };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxJsonReaderDepth*");
    }
}
