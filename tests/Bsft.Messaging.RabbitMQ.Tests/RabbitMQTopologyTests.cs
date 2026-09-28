using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using RabbitMQ.Client;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests;

public class RabbitMQTopologyTests
{
    [Fact]
    public void Names_follow_the_convention_when_nothing_is_overridden()
    {
        var topology = new RabbitMQTopology(TestOptions.Valid());

        topology.ExchangeName.Should().Be("orders.events");
        topology.DeadLetterExchangeName.Should().Be("orders.events.dlx");
        topology.QueueName.Should().Be("orders.queue");
        topology.DeadLetterQueueName.Should().Be("orders.queue.dlq");
    }

    [Fact]
    public void An_explicit_exchange_name_also_moves_the_dead_letter_exchange_derived_from_it()
    {
        var options = TestOptions.Valid();
        options.ExchangeName = "shared.events";

        var topology = new RabbitMQTopology(options);

        topology.ExchangeName.Should().Be("shared.events");
        topology.DeadLetterExchangeName.Should().Be("shared.events.dlx");
    }

    [Fact]
    public void Every_name_can_be_overridden_on_its_own()
    {
        var options = TestOptions.Valid();
        options.ExchangeName = "a";
        options.DeadLetterExchangeName = "b";
        options.QueueName = "c";
        options.DeadLetterQueueName = "d";

        var topology = new RabbitMQTopology(options);

        topology.ExchangeName.Should().Be("a");
        topology.DeadLetterExchangeName.Should().Be("b");
        topology.QueueName.Should().Be("c");
        topology.DeadLetterQueueName.Should().Be("d");
    }

    [Fact]
    public void Subscribed_event_names_are_kept_in_declaration_order()
    {
        var options = TestOptions.Valid();
        options.Subscribe("orders.placed.v1").Subscribe("orders.cancelled.v1");

        var topology = new RabbitMQTopology(options);

        topology.SubscribedEventNames.Should().Equal("orders.placed.v1", "orders.cancelled.v1");
    }

    [Fact]
    public void A_null_options_object_is_rejected()
    {
        var construct = () => new RabbitMQTopology(null!);

        construct.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void An_empty_application_name_is_rejected_because_no_name_can_be_derived_from_it()
    {
        var options = TestOptions.Valid();
        options.ApplicationName = string.Empty;

        var construct = () => new RabbitMQTopology(options);

        construct.Should().Throw<ArgumentException>()
            .WithMessage("*ApplicationName*");
    }

    [Fact]
    public void An_empty_application_name_is_accepted_once_every_name_is_given_explicitly()
    {
        var options = TestOptions.Valid();
        options.ApplicationName = string.Empty;
        options.ExchangeName = "shared.events";
        options.QueueName = "reporting.queue";

        var topology = new RabbitMQTopology(options);

        topology.ExchangeName.Should().Be("shared.events");
        topology.QueueName.Should().Be("reporting.queue");
    }

    [Fact]
    public void A_name_longer_than_amqp_allows_is_rejected()
    {
        var options = TestOptions.Valid();
        options.ExchangeName = new string('x', 256);

        var construct = () => new RabbitMQTopology(options);

        construct.Should().Throw<ArgumentException>().WithMessage("*256 characters*255*");
    }

    [Fact]
    public void A_dead_letter_exchange_equal_to_the_exchange_is_rejected_as_a_routing_loop()
    {
        var options = TestOptions.Valid();
        options.DeadLetterExchangeName = "orders.events";

        var construct = () => new RabbitMQTopology(options);

        construct.Should().Throw<ArgumentException>().WithMessage("*DeadLetterExchangeName*routed straight back*");
    }

    [Fact]
    public void A_dead_letter_queue_equal_to_the_queue_is_rejected_as_a_routing_loop()
    {
        var options = TestOptions.Valid();
        options.DeadLetterQueueName = "orders.queue";

        var construct = () => new RabbitMQTopology(options);

        construct.Should().Throw<ArgumentException>().WithMessage("*DeadLetterQueueName*routed straight back*");
    }

    [Fact]
    public void The_queue_type_is_resolved_here_too_so_the_declarer_does_not_read_the_options_again()
    {
        var options = TestOptions.Valid();
        options.QueueType = RabbitMQQueueType.Classic;

        new RabbitMQTopology(options).QueueType.Should().Be(RabbitMQQueueType.Classic);
    }

    [Fact]
    public void The_exchange_is_a_topic_exchange_unless_the_application_asks_for_the_other_routing_type()
    {
        new RabbitMQTopology(TestOptions.Valid()).ExchangeType.Should().Be(ExchangeType.Topic);

        var options = TestOptions.Valid();
        options.ExchangeType = ExchangeType.Direct;

        new RabbitMQTopology(options).ExchangeType.Should().Be(ExchangeType.Direct);
    }

    [Theory]
    [InlineData(ExchangeType.Fanout)]
    [InlineData(ExchangeType.Headers)]
    [InlineData("x-delayed-message")]
    public void An_exchange_that_does_not_route_on_the_routing_key_is_refused(string exchangeType)
    {
        // The routing key is the event name and every binding names one. A fanout exchange would deliver
        // every event to every subscribed queue and a headers exchange would deliver none, so either
        // would leave the subscriptions declared here silently meaning something else.
        var options = TestOptions.Valid();
        options.ExchangeType = exchangeType;

        var construct = () => new RabbitMQTopology(options);

        construct.Should().Throw<ArgumentException>()
            .WithMessage($"*ExchangeType is '{exchangeType}'*route on the routing key*");
    }

    [Fact]
    public void An_exchange_type_that_says_nothing_is_refused_rather_than_defaulted()
    {
        var options = TestOptions.Valid();
        options.ExchangeType = "   ";

        var construct = () => new RabbitMQTopology(options);

        construct.Should().Throw<ArgumentException>().WithMessage("*ExchangeType is required*");
    }

    [Fact]
    public void The_dead_letter_queue_is_unbounded_unless_the_application_says_what_it_keeps()
    {
        var topology = new RabbitMQTopology(TestOptions.Valid());

        topology.DeadLetterQueueMessageTtl.Should().BeNull();
        topology.DeadLetterQueueMaxLength.Should().BeNull();
    }

    [Fact]
    public void The_dead_letter_bounds_are_resolved_here_too_so_the_declarer_does_not_read_the_options_again()
    {
        var options = TestOptions.Valid();
        options.DeadLetterQueueMessageTtl = TimeSpan.FromDays(30);
        options.DeadLetterQueueMaxLength = 1_000;

        var topology = new RabbitMQTopology(options);

        topology.DeadLetterQueueMessageTtl.Should().Be(TimeSpan.FromDays(30));
        topology.DeadLetterQueueMaxLength.Should().Be(1_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_dead_letter_time_to_live_that_is_not_positive_is_rejected(int seconds)
    {
        // The broker reads a zero TTL as "expire on arrival", which would discard the last copy of every
        // event the moment parking preserved it.
        var options = TestOptions.Valid();
        options.DeadLetterQueueMessageTtl = TimeSpan.FromSeconds(seconds);

        var construct = () => new RabbitMQTopology(options);

        construct.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("DeadLetterQueueMessageTtl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_dead_letter_length_that_is_not_positive_is_rejected(long maxLength)
    {
        var options = TestOptions.Valid();
        options.DeadLetterQueueMaxLength = maxLength;

        var construct = () => new RabbitMQTopology(options);

        construct.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("DeadLetterQueueMaxLength");
    }

    [Fact]
    public void The_subscriptions_are_a_snapshot_so_a_later_change_cannot_move_a_declared_topology()
    {
        var options = TestOptions.Valid();
        options.Subscribe("orders.placed.v1");

        var topology = new RabbitMQTopology(options);
        options.Subscribe("orders.cancelled.v1");

        topology.SubscribedEventNames.Should().Equal("orders.placed.v1");
    }
}
