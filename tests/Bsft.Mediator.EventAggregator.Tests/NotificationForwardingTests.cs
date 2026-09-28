using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.EventAggregator.DependencyInjection;
using Bsft.Mediator.EventAggregator.Tests.Support;
using Bsft.Testing.Sagas;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.EventAggregator.Tests;

/// <summary>
/// End-to-end coverage of the promise the bridge makes: a notification published through the
/// mediator reaches an event-aggregator subscriber and the saga started by it.
/// </summary>
public class NotificationForwardingTests
{
    private static ISagaTestHarness BuildHarness(DeliveryTrace trace, bool forward) =>
        new SagaHarnessBuilder()
            .WithService(trace)
            .WithFlow<OrderSagaFlow>()
            .WithServices(services =>
            {
                services.AddBsftMediator(mediator =>
                {
                    if (forward)
                        mediator.ForwardNotifications();
                });
                services.AddTransient<INotificationHandler<OrderCreated>, OrderCreatedNotificationHandler>();
                services.AddBsftMediatorEventAggregator();
            })
            .Build();

    private static async Task PublishAsync(ISagaTestHarness harness, OrderCreated notification)
    {
        using var scope = harness.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.PublishAsync(notification);
    }

    [Fact]
    public async Task Forwarded_notification_reaches_an_event_aggregator_subscriber()
    {
        var trace = new DeliveryTrace();
        using var harness = BuildHarness(trace, forward: true);
        harness.Events.Subscribe<OrderCreated>(_ => trace.Add("aggregator-subscriber"));

        await PublishAsync(harness, new OrderCreated("order-1"));

        trace.Entries.Should().Contain("aggregator-subscriber");
    }

    [Fact]
    public async Task Forwarded_notification_drives_the_saga_step()
    {
        var trace = new DeliveryTrace();
        using var harness = BuildHarness(trace, forward: true);

        await PublishAsync(harness, new OrderCreated("order-1"));

        trace.Entries.Should().Contain("saga-step");
        var saga = await harness.FindSagaAsync<OrderSagaState, OrderCreated>("order-1");
        saga.Should().NotBeNull();
        saga!.State.OrderId.Should().Be("order-1");
    }

    [Fact]
    public async Task Local_handlers_run_before_the_notification_is_forwarded()
    {
        var trace = new DeliveryTrace();
        using var harness = BuildHarness(trace, forward: true);
        harness.Events.Subscribe<OrderCreated>(_ => trace.Add("aggregator-subscriber"));

        await PublishAsync(harness, new OrderCreated("order-1"));

        trace.Entries.Should().ContainInOrder("local-handler", "aggregator-subscriber");
    }

    [Fact]
    public async Task Notification_is_not_forwarded_when_forwarding_is_not_configured()
    {
        var trace = new DeliveryTrace();
        using var harness = BuildHarness(trace, forward: false);
        harness.Events.Subscribe<OrderCreated>(_ => trace.Add("aggregator-subscriber"));

        await PublishAsync(harness, new OrderCreated("order-1"));

        trace.Entries.Should().Equal("local-handler");
        (await harness.FindSagaAsync<OrderSagaState, OrderCreated>("order-1")).Should().BeNull();
    }
}
