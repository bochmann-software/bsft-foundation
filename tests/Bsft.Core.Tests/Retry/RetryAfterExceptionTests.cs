using Bsft.Core.Retry;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Retry;

public class RetryAfterExceptionTests
{
    [Fact]
    public void A_bare_instance_asks_for_a_later_attempt_without_naming_a_wait()
    {
        var exception = new RetryAfterException();

        exception.RetryAfter.Should().BeNull();
        exception.Should().BeAssignableTo<IRetryAfter>();
    }

    [Fact]
    public void A_message_alone_still_names_no_wait()
    {
        var exception = new RetryAfterException("the service is busy");

        exception.Message.Should().Be("the service is busy");
        exception.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void The_failure_underneath_is_kept()
    {
        var inner = new InvalidOperationException("the socket was closed");

        var exception = new RetryAfterException("the service is busy", inner);

        exception.InnerException.Should().BeSameAs(inner);
        exception.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void A_named_wait_is_carried()
    {
        var exception = new RetryAfterException("throttled", TimeSpan.FromSeconds(90));

        exception.RetryAfter.Should().Be(TimeSpan.FromSeconds(90));
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void A_named_wait_and_the_failure_underneath_travel_together()
    {
        var inner = new InvalidOperationException("429");

        var exception = new RetryAfterException("throttled", TimeSpan.FromMinutes(2), inner);

        exception.RetryAfter.Should().Be(TimeSpan.FromMinutes(2));
        exception.InnerException.Should().BeSameAs(inner);
    }
}
