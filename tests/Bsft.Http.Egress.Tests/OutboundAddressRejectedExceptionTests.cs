using FluentAssertions;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class OutboundAddressRejectedExceptionTests
{
    [Fact]
    public void WithoutAMessage_ItStillSaysWhatHappened() =>
        new OutboundAddressRejectedException().Message.Should().Contain("may not call");

    [Fact]
    public void WithAMessage_ItCarriesIt() =>
        new OutboundAddressRejectedException("the endpoint is internal").Message
            .Should().Be("the endpoint is internal");

    [Fact]
    public void WithACause_ItCarriesBoth()
    {
        var cause = new InvalidOperationException("underneath");

        var thrown = new OutboundAddressRejectedException("the endpoint is internal", cause);

        thrown.Message.Should().Be("the endpoint is internal");
        thrown.InnerException.Should().BeSameAs(cause);
    }
}
