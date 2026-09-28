using Bsft.Messaging.Publishing;
using Bsft.Messaging.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Tests.Publishing;

public class IntegrationEventCollectorTests
{
    [Fact]
    public void Staged_events_are_returned_in_the_order_they_were_staged()
    {
        var collector = new IntegrationEventCollector();
        var first = new OrderPlacedEvent { OrderNumber = "A" };
        var second = new OrderShippedEvent();

        collector.Enqueue(first);
        collector.Enqueue(second);

        collector.TakePending().Should().ContainInOrder(first, second);
    }

    [Fact]
    public void Taking_the_pending_events_empties_the_buffer()
    {
        var collector = new IntegrationEventCollector();
        collector.Enqueue(new OrderPlacedEvent());

        collector.TakePending().Should().HaveCount(1);

        // A second save in the same scope must not write the events of the first one again.
        collector.TakePending().Should().BeEmpty();
    }

    [Fact]
    public void An_untouched_collector_has_nothing_pending()
    {
        var collector = new IntegrationEventCollector();

        collector.TakePending().Should().BeEmpty();
    }

    [Fact]
    public void Staging_null_is_rejected()
    {
        var collector = new IntegrationEventCollector();

        var act = () => collector.Enqueue(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("integrationEvent");
    }

    [Fact]
    public void Both_sides_of_the_staging_contract_are_served_by_the_same_instance()
    {
        var collector = new IntegrationEventCollector();
        IIntegrationEventCollector staging = collector;
        IIntegrationEventBuffer buffer = collector;

        staging.Enqueue(new OrderPlacedEvent());

        buffer.TakePending().Should().HaveCount(1);
    }
}
