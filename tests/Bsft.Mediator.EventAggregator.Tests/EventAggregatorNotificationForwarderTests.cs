using Bsft.EventAggregator;
using Bsft.Mediator.EventAggregator.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.EventAggregator.Tests;

public class EventAggregatorNotificationForwarderTests
{
    [Fact]
    public void Constructor_WithNullAggregator_ThrowsArgumentNullException()
    {
        var act = () => new EventAggregatorNotificationForwarder(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("eventAggregator");
    }

    [Fact]
    public async Task ForwardAsync_WithNullNotification_ThrowsArgumentNullException()
    {
        var forwarder = new EventAggregatorNotificationForwarder(new SimpleEventAggregator());

        var act = () => forwarder.ForwardAsync<OrderCreated>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("notification");
    }

    [Fact]
    public async Task ForwardAsync_PublishesUnderTheStaticNotificationType()
    {
        var aggregator = new SimpleEventAggregator();
        var received = new List<OrderCreated>();
        aggregator.Subscribe<OrderCreated>(received.Add);
        var forwarder = new EventAggregatorNotificationForwarder(aggregator);

        await forwarder.ForwardAsync(new OrderCreated("order-1"));

        received.Should().ContainSingle().Which.OrderId.Should().Be("order-1");
    }

    [Fact]
    public async Task ForwardAsync_PropagatesASubscriberFailure()
    {
        var aggregator = new SimpleEventAggregator();
        aggregator.Subscribe<OrderCreated>(_ => throw new InvalidOperationException("subscriber is broken"));
        var forwarder = new EventAggregatorNotificationForwarder(aggregator);

        var act = () => forwarder.ForwardAsync(new OrderCreated("order-1"));

        await act.Should().ThrowAsync<AggregateException>();
    }

    [Fact]
    public async Task ForwardAsync_ObservesTheCancellationToken()
    {
        var aggregator = new SimpleEventAggregator();
        aggregator.Subscribe<OrderCreated>(_ => { });
        var forwarder = new EventAggregatorNotificationForwarder(aggregator);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => forwarder.ForwardAsync(new OrderCreated("order-1"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Forwarder_IsANotificationForwarder()
    {
        var forwarder = new EventAggregatorNotificationForwarder(new SimpleEventAggregator());

        forwarder.Should().BeAssignableTo<INotificationForwarder>();
    }
}
