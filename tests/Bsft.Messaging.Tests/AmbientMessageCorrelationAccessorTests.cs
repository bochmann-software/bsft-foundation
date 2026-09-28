using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Tests;

public class AmbientMessageCorrelationAccessorTests
{
    private readonly MessageCorrelationScope _scope = new();

    [Fact]
    public void Without_the_correlation_bridge_an_event_staged_from_a_request_carries_no_id()
    {
        new AmbientMessageCorrelationAccessor(_scope).CorrelationId.Should().BeNull();
    }

    [Fact]
    public void An_event_staged_while_handling_another_one_keeps_that_events_id()
    {
        var accessor = new AmbientMessageCorrelationAccessor(_scope);

        using (_scope.Use("message-chain-1"))
        {
            accessor.CorrelationId.Should().Be("message-chain-1");
        }
    }

    [Fact]
    public void The_scope_is_required()
    {
        var act = () => new AmbientMessageCorrelationAccessor(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("scope");
    }
}
