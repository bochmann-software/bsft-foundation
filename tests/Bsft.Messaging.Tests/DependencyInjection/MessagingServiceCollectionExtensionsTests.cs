using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.Loopback;
using Bsft.Messaging.Outbox;
using Bsft.Messaging.Publishing;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Outbox;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Messaging.Tests.DependencyInjection;

public class MessagingServiceCollectionExtensionsTests
{
    [Fact]
    public void The_messaging_core_resolves_from_the_container()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageBus>().Should().BeOfType<InMemoryMessageBus>();
        provider.GetRequiredService<IIntegrationEventTypeRegistry>()
            .FindType("orders.placed.v1").Should().Be<OrderPlacedEvent>();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        provider.GetRequiredService<IIntegrationEventSerializer>().Should().BeOfType<JsonIntegrationEventSerializer>();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMessageCorrelationAccessor>()
            .Should().BeOfType<AmbientMessageCorrelationAccessor>();
    }

    /// <summary>
    /// The outbox runs on the shared kernel, and the bounds a host configures under
    /// <c>Bsft:Messaging:Outbox</c> are what the pass is bounded by. Nothing else in the container
    /// would notice if that mapping were dropped, so the lease the pass asks for is what pins it.
    /// </summary>
    [Fact]
    public async Task The_outbox_drain_pass_is_bounded_by_the_configured_outbox_group()
    {
        var store = new FakeOutboxStore();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxStore>(store);

        services.AddBsftMessaging(o =>
        {
            o.Outbox.BatchSize = 7;
            o.Outbox.LeaseDuration = TimeSpan.FromMinutes(3);
        });

        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IOutboxDrainer<IntegrationEventMessage>>().DrainOnceAsync();

        store.LastBatchSize.Should().Be(7);
        store.LastLeaseDuration.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void The_default_correlation_accessor_is_scoped_like_the_bridge_that_replaces_it()
    {
        var services = new ServiceCollection();

        services.AddBsftMessaging();

        services.Single(d => d.ServiceType == typeof(IMessageCorrelationAccessor))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void The_ambient_correlation_scope_is_one_instance_for_the_whole_container()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging();
        using var provider = services.BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // The value flows with the asynchronous control flow, so the component restoring it and the
        // ones reading it have to share the instance holding it.
        first.ServiceProvider.GetRequiredService<IMessageCorrelationScope>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<IMessageCorrelationScope>());
    }

    [Fact]
    public void Staging_and_draining_share_one_instance_per_scope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging();
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var staging = scope.ServiceProvider.GetRequiredService<IIntegrationEventCollector>();
        var buffer = scope.ServiceProvider.GetRequiredService<IIntegrationEventBuffer>();
        staging.Enqueue(new OrderPlacedEvent());

        buffer.Should().BeSameAs(staging);
        buffer.TakePending().Should().HaveCount(1);
    }

    [Fact]
    public void Another_scope_has_its_own_staging_area()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging();
        using var provider = services.BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        first.ServiceProvider.GetRequiredService<IIntegrationEventCollector>().Enqueue(new OrderPlacedEvent());

        second.ServiceProvider.GetRequiredService<IIntegrationEventBuffer>().TakePending().Should().BeEmpty();
    }

    [Fact]
    public void A_broker_adapter_registered_first_keeps_its_place()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var broker = new FakeMessageBus();
        services.AddSingleton<IMessageBus>(broker);

        services.AddBsftMessaging();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageBus>().Should().BeSameAs(broker);
    }

    [Fact]
    public void Handlers_are_registered_side_by_side_rather_than_replacing_each_other()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging();

        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, RecordingOrderPlacedHandler>();
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, FaultingOrderPlacedHandler>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<OrderPlacedEvent>>().Should().HaveCount(2);
    }

    [Fact]
    public void Registering_the_same_handler_twice_does_not_deliver_the_event_twice()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging();

        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, RecordingOrderPlacedHandler>();
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, RecordingOrderPlacedHandler>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<OrderPlacedEvent>>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_published_event_reaches_a_scoped_handler_resolved_by_the_bus()
    {
        var log = new DeliveryLog();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(log);
        services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));
        services.AddBsftIntegrationEventHandler<OrderPlacedEvent, ScopedOrderPlacedHandler>();
        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IIntegrationEventSerializer>();
        var integrationEvent = new OrderPlacedEvent { OrderNumber = "A-1" };

        await provider.GetRequiredService<IMessageBus>().PublishAsync(new IntegrationEventMessage
        {
            EventId = integrationEvent.EventId,
            EventName = "orders.placed.v1",
            OccurredAt = integrationEvent.OccurredAt,
            Payload = serializer.Serialize(integrationEvent),
        });

        log.Entries.Should().ContainSingle().Which.Should().Be("A-1");
    }

    [Fact]
    public void A_null_service_collection_is_rejected()
    {
        var addCore = () => MessagingServiceCollectionExtensions.AddBsftMessaging(null!);
        var addHandler = () =>
            MessagingServiceCollectionExtensions.AddBsftIntegrationEventHandler<OrderPlacedEvent, RecordingOrderPlacedHandler>(null!);

        addCore.Should().Throw<ArgumentNullException>().WithParameterName("services");
        addHandler.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
