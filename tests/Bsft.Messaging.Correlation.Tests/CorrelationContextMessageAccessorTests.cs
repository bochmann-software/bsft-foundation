using Bsft.Correlation;
using Bsft.Messaging.Correlation.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Correlation.Tests;

public class CorrelationContextMessageAccessorTests
{
    [Fact]
    public void ConstructorRejectsANullCorrelationContext()
    {
        var act = () => new CorrelationContextMessageAccessor(null!, new FakeMessageCorrelationScope());

        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationContext");
    }

    [Fact]
    public void ConstructorRejectsANullMessageCorrelationScope()
    {
        var act = () => new CorrelationContextMessageAccessor(new CorrelationContext(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("messageCorrelationScope");
    }

    [Fact]
    public void OutsideADeliveryTheScopesOwnCorrelationIdIsUsed()
    {
        var accessor = new CorrelationContextMessageAccessor(
            new CorrelationContext { CorrelationId = "request-1" }, new FakeMessageCorrelationScope());

        accessor.CorrelationId.Should().Be("request-1");
    }

    [Fact]
    public void InsideADeliveryTheAmbientIdWinsOverTheScopesOwnId()
    {
        var scope = new FakeMessageCorrelationScope();
        var accessor = new CorrelationContextMessageAccessor(
            new CorrelationContext { CorrelationId = "fresh-scope-id" }, scope);

        // A handler staging the next integration event must not start a new correlation on every hop,
        // even though the delivery's own service scope mints a fresh id.
        using (scope.Use("message-chain-1"))
        {
            accessor.CorrelationId.Should().Be("message-chain-1");
        }

        accessor.CorrelationId.Should().Be("fresh-scope-id", "the ambient id only applies while a message is handled");
    }

    [Fact]
    public void AnAmbientIdOfNullFallsBackToTheScopesOwnId()
    {
        var scope = new FakeMessageCorrelationScope();
        var accessor = new CorrelationContextMessageAccessor(
            new CorrelationContext { CorrelationId = "request-1" }, scope);

        using (scope.Use(null))
        {
            accessor.CorrelationId.Should().Be("request-1");
        }
    }
}
