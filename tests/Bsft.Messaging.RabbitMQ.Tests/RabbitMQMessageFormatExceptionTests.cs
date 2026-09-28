using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests;

public class RabbitMQMessageFormatExceptionTests
{
    [Fact]
    public void Default_constructor_creates_an_exception_without_an_inner_failure()
    {
        var exception = new RabbitMQMessageFormatException();

        exception.Message.Should().NotBeNullOrWhiteSpace();
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Message_constructor_keeps_the_message()
    {
        var exception = new RabbitMQMessageFormatException("the message id is not a guid");

        exception.Message.Should().Be("the message id is not a guid");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Inner_exception_is_kept_so_the_original_failure_is_not_lost()
    {
        var cause = new FormatException("unparsable");

        var exception = new RabbitMQMessageFormatException("the delivery is malformed", cause);

        exception.Message.Should().Be("the delivery is malformed");
        exception.InnerException.Should().BeSameAs(cause);
    }
}
