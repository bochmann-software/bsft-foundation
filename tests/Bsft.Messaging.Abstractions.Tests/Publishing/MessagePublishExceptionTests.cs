using Bsft.Messaging.Publishing;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Abstractions.Tests.Publishing;

public class MessagePublishExceptionTests
{
    [Fact]
    public void Default_constructor_creates_an_exception_without_an_inner_failure()
    {
        var exception = new MessagePublishException();

        exception.Message.Should().NotBeNullOrWhiteSpace();
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Message_constructor_keeps_the_message()
    {
        var exception = new MessagePublishException("publish failed");

        exception.Message.Should().Be("publish failed");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Inner_exception_is_kept_so_the_original_failure_is_not_lost()
    {
        var cause = new InvalidOperationException("handler blew up");

        var exception = new MessagePublishException("publish failed", cause);

        exception.Message.Should().Be("publish failed");
        exception.InnerException.Should().BeSameAs(cause);
    }
}
