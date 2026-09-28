using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests;

/// <summary>
/// The guard clause of the startup validator. Every rule it enforces is exercised end to end through
/// <c>AddBsftRabbitMQMessaging</c>; what is left here is the argument the options infrastructure never
/// passes, which is exactly why it needs a test of its own.
/// </summary>
public sealed class BsftRabbitMQOptionsValidatorTests
{
    [Fact]
    public void Validate_NullOptions_Throws()
    {
        var validator = new BsftRabbitMQOptionsValidator();

        var act = () => validator.Validate(name: null, options: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
