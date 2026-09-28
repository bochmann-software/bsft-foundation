using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests;

public class BsftRabbitMQOptionsTests
{
    [Fact]
    public void Defaults_are_the_documented_conventions()
    {
        var options = new BsftRabbitMQOptions();

        options.HostName.Should().Be("localhost");
        options.Port.Should().Be(5672);
        options.VirtualHost.Should().Be("/");
        options.PrefetchCount.Should().Be(20);
        options.PublisherConfirmTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.NetworkRecoveryInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.ContentType.Should().Be("application/json");
        options.SubscribedEventNames.Should().BeEmpty();
    }

    [Fact]
    public void Credentials_have_no_default_so_a_forgotten_binding_cannot_pass_silently()
    {
        var options = new BsftRabbitMQOptions();

        options.UserName.Should().BeNull();
        options.Password.Should().BeNull();
    }

    [Fact]
    public void Subscribe_records_the_event_name_and_returns_the_options_for_chaining()
    {
        var options = new BsftRabbitMQOptions();

        var returned = options.Subscribe("orders.placed.v1").Subscribe("orders.cancelled.v1");

        returned.Should().BeSameAs(options);
        options.SubscribedEventNames.Should().Equal("orders.placed.v1", "orders.cancelled.v1");
    }

    [Fact]
    public void Subscribe_ignores_a_name_that_is_already_declared()
    {
        var options = new BsftRabbitMQOptions();

        options.Subscribe("orders.placed.v1").Subscribe("orders.placed.v1");

        options.SubscribedEventNames.Should().ContainSingle();
    }

    [Fact]
    public void The_queue_is_a_quorum_queue_unless_the_application_says_otherwise()
    {
        // It is the only queue type that reports x-delivery-count, which is what makes the consumer
        // host's attempt budget mean anything.
        new BsftRabbitMQOptions().QueueType.Should().Be(RabbitMQQueueType.Quorum);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Subscribe_rejects_an_empty_event_name(string? eventName)
    {
        var options = new BsftRabbitMQOptions();

        var subscribe = () => options.Subscribe(eventName!);

        subscribe.Should().Throw<ArgumentException>().WithParameterName("eventName");
    }
}
