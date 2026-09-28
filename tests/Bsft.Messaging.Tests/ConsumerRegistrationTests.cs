using Bsft.Messaging.Tests.Fakes;
using Bsft.Instrumentation;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Messaging.Consuming;
using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.Diagnostics;
using Bsft.Messaging.Inbox;
using Bsft.Messaging.Loopback;

namespace Bsft.Messaging.Tests;

public class ConsumerRegistrationTests
{
    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // The consuming side builds on the messaging core: the decoder resolves names through its
        // registry, and the guarded delivery runs handlers through its invoker and correlation scope.
        services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));

        return services;
    }

    [Fact]
    public void The_inbox_resolves_with_the_in_memory_store_by_default()
    {
        var services = CreateServices();

        services.AddBsftInbox(o => o.ConsumerName = "orders");

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<BsftMessagingOptions>>().Value.Consumer.ConsumerName
            .Should().Be("orders");
        provider.GetRequiredService<IInboxGuardedDelivery>().Should().BeOfType<InboxGuardedDelivery>();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInboxStore>().Should().BeOfType<InMemoryInboxStore>();
    }

    [Fact]
    public void The_inbox_store_is_scoped_so_it_can_share_the_handler_s_unit_of_work()
    {
        var services = CreateServices();

        services.AddBsftInbox();

        services.Single(d => d.ServiceType == typeof(IInboxStore))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Each_delivery_scope_gets_its_own_store_over_one_shared_ledger()
    {
        var services = CreateServices();
        services.AddBsftInbox();
        using var provider = services.BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<IInboxStore>()
            .Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<IInboxStore>());
        first.ServiceProvider.GetRequiredService<InMemoryInboxLedger>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<InMemoryInboxLedger>());
    }

    [Fact]
    public void A_durable_store_registered_first_keeps_its_place()
    {
        var services = CreateServices();
        services.AddScoped<IInboxStore>(_ => new FakeInboxStore());

        services.AddBsftInbox();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInboxStore>().Should().BeOfType<FakeInboxStore>();
    }

    [Fact]
    public void The_consumer_host_resolves_with_everything_it_needs()
    {
        var services = CreateServices();

        // The host consumes from a transport; without one registered it has nothing to receive from.
        services.AddBsftMessagingLoopback();
        services.AddBsftConsumerHost(o => o.ConsumerName = "orders");

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IIncomingMessageDecoder>().Should().BeOfType<IncomingMessageDecoder>();
        provider.GetRequiredService<IIntegrationEventConsumer>().Should().BeOfType<IntegrationEventConsumer>();
        provider.GetRequiredService<IConsumerTelemetry>().Should().BeOfType<ConsumerTelemetry>();
        provider.GetServices<IHostedService>().OfType<IntegrationEventConsumerHost>().Should().ContainSingle();
    }

    [Fact]
    public void The_consumer_host_advertises_its_instrumentation_source()
    {
        var services = CreateServices();

        services.AddBsftConsumerHost(o => o.ConsumerName = "orders");

        using var provider = services.BuildServiceProvider();
        provider.GetServices<ObservabilitySourceRegistration>()
            .Should().ContainSingle(r => r.Name == ConsumerTelemetry.SourceName);
    }

    [Fact]
    public void The_consumer_host_configures_the_inbox_once_rather_than_replacing_what_was_configured()
    {
        var services = CreateServices();

        services.AddBsftConsumerHost(o => o.ConsumerName = "orders");
        services.AddBsftInbox(o => o.MaxDeliveryAttempts = 3);

        // Both delegates apply: the host calls AddBsftInbox rather than repeating it, and a second
        // call adds to the configuration instead of discarding the first one's.
        using var provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService<IOptions<BsftMessagingOptions>>().Value.Consumer;

        consumer.ConsumerName.Should().Be("orders");
        consumer.MaxDeliveryAttempts.Should().Be(3);
    }

    [Fact]
    public async Task The_loopback_serves_as_both_the_bus_and_the_source()
    {
        // Built from scratch so the loopback is registered before the messaging core, which is the
        // documented order and the only one in which it takes the bus.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessagingLoopback();
        services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));
        using var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<IMessageBus>();
        var source = provider.GetRequiredService<IMessageSource>();

        // Registered before AddBsftMessaging, so the loopback takes the bus rather than the in-memory
        // one that handles inside the publish.
        bus.Should().BeOfType<LoopbackMessageTransport>().And.BeSameAs(source);

        var message = new IntegrationEventMessage
        {
            EventId = Guid.NewGuid(),
            EventName = "orders.placed.v1",
            OccurredAt = DateTimeOffset.UnixEpoch,
            Payload = "{}",
        };
        await bus.PublishAsync(message);

        provider.GetRequiredService<LoopbackMessageTransport>().Complete();
        var drained = new List<IMessageDelivery>();
        await foreach (var delivery in source.ReceiveAsync())
            drained.Add(delivery);

        drained.Should().ContainSingle().Which.Message.Should().BeSameAs(message);
    }

    [Fact]
    public void The_in_memory_bus_still_wins_when_the_loopback_is_added_afterwards()
    {
        // The documented ordering rule, the same one a broker adapter follows: CreateServices() has already
        // registered the core, so a loopback added now is too late to replace the bus.
        var services = CreateServices();

        services.AddBsftMessagingLoopback();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageBus>().Should().BeOfType<InMemoryMessageBus>();
    }

    [Fact]
    public void A_null_service_collection_is_rejected()
    {
        var addInbox = () => MessagingServiceCollectionExtensions.AddBsftInbox(null!);
        var addHost = () => MessagingServiceCollectionExtensions.AddBsftConsumerHost(null!);
        var addLoopback = () => MessagingServiceCollectionExtensions.AddBsftMessagingLoopback(null!);

        addInbox.Should().Throw<ArgumentNullException>().WithParameterName("services");
        addHost.Should().Throw<ArgumentNullException>().WithParameterName("services");
        addLoopback.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
