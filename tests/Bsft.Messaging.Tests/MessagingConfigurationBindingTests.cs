using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.Tests;

public class MessagingConfigurationBindingTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public void The_section_name_is_the_family_section()
    {
        BsftMessagingOptions.SectionName.Should().Be("Bsft:Messaging");
    }

    [Fact]
    public void Both_option_groups_bind_from_the_section()
    {
        var configuration = Configuration(
            ($"{BsftMessagingOptions.SectionName}:Consumer:ConsumerName", "orders"),
            ($"{BsftMessagingOptions.SectionName}:Consumer:MaxDeliveryAttempts", "3"),
            ($"{BsftMessagingOptions.SectionName}:Consumer:InboxRetention", "7.00:00:00"),
            ($"{BsftMessagingOptions.SectionName}:Outbox:BatchSize", "42"),
            ($"{BsftMessagingOptions.SectionName}:Outbox:PollInterval", "00:00:02"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftMessagingOptions>>().Value;

        options.Consumer.ConsumerName.Should().Be("orders");
        options.Consumer.MaxDeliveryAttempts.Should().Be(3);
        options.Consumer.InboxRetention.Should().Be(TimeSpan.FromDays(7));
        options.Outbox.BatchSize.Should().Be(42);
        options.Outbox.PollInterval.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_delegate_runs_after_the_section_so_code_wins()
    {
        var configuration = Configuration(
            ($"{BsftMessagingOptions.SectionName}:Consumer:ConsumerName", "from-configuration"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging(configuration, o =>
        {
            o.Consumer.ConsumerName = "from-code";
            o.AddEvent<OrderPlacedEvent>("orders.placed.v1");
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftMessagingOptions>>().Value;

        options.Consumer.ConsumerName.Should().Be("from-code");
        options.RegisteredEventTypes.Keys.Should().Contain(typeof(OrderPlacedEvent));
    }

    [Fact]
    public void An_absent_section_leaves_every_default_in_place()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging(Configuration());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftMessagingOptions>>().Value;

        options.Consumer.MaxDeliveryAttempts.Should().Be(10);
        options.Outbox.BatchSize.Should().Be(100);
    }

    [Fact]
    public void A_value_the_validator_refuses_fails_when_the_options_are_resolved()
    {
        var configuration = Configuration(
            ($"{BsftMessagingOptions.SectionName}:Outbox:BatchSize", "0"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging(configuration);

        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetRequiredService<IOptions<BsftMessagingOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*BatchSize*");
    }

    [Fact]
    public void A_null_argument_is_rejected()
    {
        var withoutServices = () =>
            MessagingServiceCollectionExtensions.AddBsftMessaging(null!, Configuration());
        var withoutConfiguration = () =>
            new ServiceCollection().AddBsftMessaging((IConfiguration)null!);

        withoutServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        withoutConfiguration.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }
}
