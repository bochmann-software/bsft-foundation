using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Abstractions.Tests;

public class MessageDecodeExceptionTests
{
    [Fact]
    public void The_standard_constructors_report_an_undecodable_payload()
    {
        // The default is the reason that fits an instance created without one: a payload that did not
        // become an event.
        new MessageDecodeException().ParkReason.Should().Be(MessageParkReason.UndecodablePayload);
        new MessageDecodeException("broken").Message.Should().Be("broken");

        var wrapped = new MessageDecodeException("broken", new InvalidOperationException("inner"));
        wrapped.InnerException.Should().BeOfType<InvalidOperationException>();
        wrapped.ParkReason.Should().Be(MessageParkReason.UndecodablePayload);
    }

    [Fact]
    public void The_reason_constructor_keeps_the_reason_and_the_inner_failure()
    {
        var refusal = new MessageDecodeException(
            MessageParkReason.UnknownEventName, "nobody declared it", new InvalidOperationException("inner"));

        refusal.ParkReason.Should().Be(MessageParkReason.UnknownEventName);
        refusal.Message.Should().Be("nobody declared it");
        refusal.InnerException.Should().BeOfType<InvalidOperationException>();
    }
}
