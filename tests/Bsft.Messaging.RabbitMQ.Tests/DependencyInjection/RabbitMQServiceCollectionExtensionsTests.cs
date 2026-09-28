using Bsft.Messaging.Consuming;
using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.Loopback;
using Bsft.Messaging.RabbitMQ.Consuming;
using Bsft.Messaging.RabbitMQ.DependencyInjection;
using Bsft.Messaging.RabbitMQ.Publishing;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests.DependencyInjection;

public class RabbitMQServiceCollectionExtensionsTests
{
    [Fact]
    public void A_null_argument_is_rejected()
    {
        var withoutServices = () => RabbitMQServiceCollectionExtensions.AddBsftRabbitMQMessaging(null!, _ => { });
        var withoutConfigure = () => new ServiceCollection().AddBsftRabbitMQMessaging((Action<BsftRabbitMQOptions>)null!);

        withoutServices.Should().Throw<ArgumentNullException>();
        withoutConfigure.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Registering_the_adapter_before_the_messaging_core_replaces_the_in_memory_bus()
    {
        var services = CreateServices();

        services.AddBsftRabbitMQMessaging(Configure);
        services.AddBsftMessaging();

        // Disposed asynchronously because the publishing channel provider is only IAsyncDisposable:
        // closing a channel is a conversation with the broker, and blocking a thread on it would be
        // the wrong way to end one. The generic host disposes its provider asynchronously.
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageBus>().Should().BeOfType<RabbitMQMessageBus>();
    }

    [Fact]
    public void Registering_the_adapter_after_the_messaging_core_leaves_the_in_memory_bus_in_place()
    {
        var services = CreateServices();

        services.AddBsftMessaging();
        services.AddBsftRabbitMQMessaging(Configure);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageBus>().Should().BeOfType<InMemoryMessageBus>();
    }

    [Fact]
    public async Task The_bus_is_a_singleton_so_one_connection_serves_the_whole_host()
    {
        var services = CreateServices();
        services.AddBsftRabbitMQMessaging(Configure);
        services.AddBsftMessaging();

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMessageBus>().Should().BeSameAs(provider.GetRequiredService<IMessageBus>());
    }

    [Fact]
    public async Task Registering_the_adapter_before_the_messaging_core_also_supplies_the_receiving_half()
    {
        // Both halves are TryAdd'd by the core — the loopback transport registers an IMessageSource the
        // same way — so the receiving side is decided by the same ordering rule as the sending one.
        var services = CreateServices();

        services.AddBsftRabbitMQMessaging(Configure);
        services.AddBsftMessaging();
        services.AddBsftMessagingLoopback();

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageSource>().Should().BeOfType<RabbitMQMessageSource>();
    }

    [Fact]
    public async Task Registering_the_loopback_first_leaves_the_adapter_without_either_half()
    {
        var services = CreateServices();

        services.AddBsftMessagingLoopback();
        services.AddBsftRabbitMQMessaging(Configure);

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageBus>().Should().BeOfType<LoopbackMessageTransport>();
        provider.GetRequiredService<IMessageSource>().Should().BeOfType<LoopbackMessageTransport>();
    }

    [Fact]
    public async Task The_source_is_a_singleton_so_one_subscription_serves_the_whole_host()
    {
        var services = CreateServices();
        services.AddBsftRabbitMQMessaging(Configure);
        services.AddBsftMessaging();

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMessageSource>().Should().BeSameAs(provider.GetRequiredService<IMessageSource>());
    }

    [Fact]
    public async Task The_consumer_host_can_be_constructed_against_the_adapters_source()
    {
        // The seam is only bound if the container can actually put the two sides together.
        var services = CreateServices();
        services.AddBsftRabbitMQMessaging(Configure);
        services.AddBsftMessaging();
        services.AddBsftConsumerHost(o => o.ConsumerName = "orders");

        await using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().OfType<IntegrationEventConsumerHost>().Should().ContainSingle();
    }

    [Fact]
    public void The_resolved_names_and_settings_are_registered_so_nothing_re_derives_them()
    {
        var services = CreateServices();
        services.AddBsftRabbitMQMessaging(o =>
        {
            Configure(o);
            o.Subscribe("orders.placed.v1");
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<RabbitMQTopology>().ExchangeName.Should().Be("orders.events");
        provider.GetRequiredService<RabbitMQTopology>().SubscribedEventNames.Should().Equal("orders.placed.v1");
        provider.GetRequiredService<RabbitMQConnectionSettings>().ApplicationName.Should().Be("orders");
    }

    [Fact]
    public void The_options_are_validated_at_start_up_rather_than_on_the_first_publish()
    {
        var services = CreateServices();
        services.AddBsftRabbitMQMessaging(o => o.ApplicationName = "orders");

        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetRequiredService<IOptions<BsftRabbitMQOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*UserName is required*");
    }

    [Fact]
    public void A_numeric_option_out_of_range_also_fails_validation()
    {
        var services = CreateServices();
        services.AddBsftRabbitMQMessaging(o =>
        {
            Configure(o);
            o.PrefetchCount = 0;
        });

        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetRequiredService<IOptions<BsftRabbitMQOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*PrefetchCount*");
    }

    [Fact]
    public void Both_failures_of_one_misconfiguration_are_reported_together()
    {
        // One from the connection settings and one from the topology, so a host is told everything
        // that is wrong with its section instead of one thing per restart.
        var services = CreateServices();
        services.AddBsftRabbitMQMessaging(o =>
        {
            Configure(o);
            o.Password = null;
            o.DeadLetterQueueName = "orders.queue";
        });

        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetRequiredService<IOptions<BsftRabbitMQOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().HaveCount(2);
    }

    [Fact]
    public void Registering_twice_applies_both_configurations_in_order()
    {
        // The options are a Configure chain rather than a single resolved instance, so the later call
        // refines what the earlier one set instead of being discarded by a TryAdd.
        var services = CreateServices();

        services.AddBsftRabbitMQMessaging(Configure);
        services.AddBsftRabbitMQMessaging(o => o.ApplicationName = "reporting");

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<RabbitMQTopology>().ExchangeName.Should().Be("reporting.events");
    }

    [Fact]
    public void The_configuration_overload_binds_the_section_before_the_delegate_runs()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftRabbitMQOptions.SectionName}:ApplicationName"] = "orders",
                [$"{BsftRabbitMQOptions.SectionName}:UserName"] = TestOptions.UserName,
                [$"{BsftRabbitMQOptions.SectionName}:Password"] = TestOptions.Password,
                [$"{BsftRabbitMQOptions.SectionName}:HostName"] = "broker.example.com",
            })
            .Build();

        var services = CreateServices();
        services.AddBsftRabbitMQMessaging(configuration, o => o.Port = 5671);

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<RabbitMQConnectionSettings>();

        settings.ApplicationName.Should().Be("orders");
        settings.HostName.Should().Be("broker.example.com");
        settings.UserName.Should().Be(TestOptions.UserName);
        settings.Port.Should().Be(5671);
    }

    [Fact]
    public void The_configuration_overload_rejects_a_null_argument()
    {
        var withoutServices = () =>
            RabbitMQServiceCollectionExtensions.AddBsftRabbitMQMessaging(null!, new ConfigurationBuilder().Build());
        var withoutConfiguration = () =>
            new ServiceCollection().AddBsftRabbitMQMessaging((IConfiguration)null!);

        withoutServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        withoutConfiguration.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void The_extension_returns_the_collection_for_chaining()
    {
        var services = CreateServices();

        services.AddBsftRabbitMQMessaging(Configure).Should().BeSameAs(services);
    }

    private static void Configure(BsftRabbitMQOptions options)
    {
        options.ApplicationName = "orders";
        options.UserName = TestOptions.UserName;
        options.Password = TestOptions.Password;
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return services;
    }
}
