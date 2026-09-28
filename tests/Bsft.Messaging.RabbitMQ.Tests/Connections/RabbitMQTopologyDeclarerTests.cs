using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Xunit;
using Bsft.Messaging.RabbitMQ.Connections;

namespace Bsft.Messaging.RabbitMQ.Tests.Connections;

public class RabbitMQTopologyDeclarerTests
{
    private readonly FakeRabbitMQChannel _channel = new();
    private readonly RecordingLogger _log = new();

    [Fact]
    public void A_null_dependency_is_rejected()
    {
        var topology = new RabbitMQTopology(TestOptions.Valid());

        var withoutTopology = () => new RabbitMQTopologyDeclarer(
            null!, NullLogger<RabbitMQTopologyDeclarer>.Instance);
        var withoutLogger = () => new RabbitMQTopologyDeclarer(topology, null!);

        withoutTopology.Should().Throw<ArgumentNullException>();
        withoutLogger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_null_channel_is_rejected_by_both_declarations()
    {
        var declarer = Declarer();

        var publish = () => declarer.DeclarePublishTopologyAsync(null!);
        var consume = () => declarer.DeclareConsumeTopologyAsync(null!);

        await publish.Should().ThrowAsync<ArgumentNullException>();
        await consume.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Publishing_declares_the_topic_exchange_and_nothing_a_consumer_owns()
    {
        await Declarer().DeclarePublishTopologyAsync(_channel);

        _channel.DeclaredExchanges.Should().Equal(("orders.events", ExchangeType.Topic));
        _channel.DeclaredQueues.Should().BeEmpty();
        _channel.Bindings.Should().BeEmpty();
    }

    [Fact]
    public async Task Consuming_declares_both_exchanges_as_topic_exchanges()
    {
        await Declarer().DeclareConsumeTopologyAsync(_channel);

        _channel.DeclaredExchanges.Should().Equal(
            ("orders.events", ExchangeType.Topic),
            ("orders.events.dlx", ExchangeType.Topic));
    }

    [Fact]
    public async Task The_queue_points_at_the_dead_letter_exchange_and_keeps_its_routing_key()
    {
        await Declarer().DeclareConsumeTopologyAsync(_channel);

        var queue = _channel.DeclaredQueues.Should().ContainSingle(q => q.Queue == "orders.queue").Subject;

        queue.Arguments.Should().ContainKey(RabbitMQTopologyDeclarer.DeadLetterExchangeArgument)
            .WhoseValue.Should().Be("orders.events.dlx");
        queue.Arguments.Should().NotContainKey("x-dead-letter-routing-key");
    }

    [Fact]
    public async Task The_queue_is_a_quorum_queue_by_default_because_only_that_one_counts_redeliveries()
    {
        // DeliveryAttempt is read from x-delivery-count, which a classic queue never writes. Without
        // it the consumer host's attempt budget is never reached and a poison message loops forever.
        await Declarer().DeclareConsumeTopologyAsync(_channel);

        var queue = _channel.DeclaredQueues.Should().ContainSingle(q => q.Queue == "orders.queue").Subject;

        queue.Arguments.Should().ContainKey(RabbitMQTopologyDeclarer.QueueTypeArgument)
            .WhoseValue.Should().Be(RabbitMQTopologyDeclarer.QuorumQueueType);
    }

    [Fact]
    public async Task A_deployment_that_cannot_have_quorum_queues_can_ask_for_a_classic_one()
    {
        var options = TestOptions.Valid();
        options.QueueType = RabbitMQQueueType.Classic;

        await Declarer(options).DeclareConsumeTopologyAsync(_channel);

        var queue = _channel.DeclaredQueues.Should().ContainSingle(q => q.Queue == "orders.queue").Subject;

        queue.Arguments.Should().ContainKey(RabbitMQTopologyDeclarer.QueueTypeArgument)
            .WhoseValue.Should().Be(RabbitMQTopologyDeclarer.ClassicQueueType);
    }

    [Fact]
    public async Task The_dead_letter_queue_takes_no_queue_type_because_nothing_redelivers_from_it()
    {
        await Declarer().DeclareConsumeTopologyAsync(_channel);

        _channel.DeclaredQueues.Should().Contain(q => q.Queue == "orders.queue.dlq" && q.Arguments == null);
    }

    [Fact]
    public async Task The_dead_letter_queue_catches_every_routing_key()
    {
        await Declarer().DeclareConsumeTopologyAsync(_channel);

        _channel.DeclaredQueues.Should().Contain(q => q.Queue == "orders.queue.dlq" && q.Arguments == null);
        _channel.Bindings.Should().Contain(("orders.queue.dlq", "orders.events.dlx", "#"));
    }

    [Fact]
    public async Task The_queue_is_bound_once_per_subscribed_event_name()
    {
        var options = TestOptions.Valid();
        options.Subscribe("orders.placed.v1").Subscribe("orders.cancelled.v1");

        await Declarer(options).DeclareConsumeTopologyAsync(_channel);

        _channel.Bindings.Should().Contain(("orders.queue", "orders.events", "orders.placed.v1"));
        _channel.Bindings.Should().Contain(("orders.queue", "orders.events", "orders.cancelled.v1"));
    }

    [Fact]
    public async Task A_consumer_without_subscriptions_still_gets_its_queue_but_no_binding()
    {
        await Declarer().DeclareConsumeTopologyAsync(_channel);

        _channel.DeclaredQueues.Should().Contain(q => q.Queue == "orders.queue");
        _channel.Bindings.Should().OnlyContain(b => b.Queue == "orders.queue.dlq");
    }

    [Fact]
    public async Task Declaring_twice_asks_for_exactly_the_same_objects_so_the_broker_treats_it_as_a_no_op()
    {
        var options = TestOptions.Valid();
        options.Subscribe("orders.placed.v1");
        var declarer = Declarer(options);
        var second = new FakeRabbitMQChannel();

        await declarer.DeclareConsumeTopologyAsync(_channel);
        await declarer.DeclareConsumeTopologyAsync(second);

        second.DeclaredExchanges.Should().Equal(_channel.DeclaredExchanges);
        second.Bindings.Should().Equal(_channel.Bindings);
        second.DeclaredQueues.Select(q => q.Queue).Should().Equal(_channel.DeclaredQueues.Select(q => q.Queue));
    }

    [Fact]
    public async Task A_classic_queue_is_declared_with_a_warning_that_the_attempt_budget_cannot_work()
    {
        // Nothing throws and nothing is dropped, so the failure this warns about is otherwise
        // invisible: MaxDeliveryAttempts reads as configured and is never reached.
        var options = TestOptions.Valid();
        options.QueueType = RabbitMQQueueType.Classic;

        await Declarer(options).DeclareConsumeTopologyAsync(_channel);

        _log.Warnings.Should().ContainSingle()
            .Which.Should().Contain("orders.queue").And.Contain(RabbitMQMessageMapper.DeliveryCountHeader);
    }

    [Fact]
    public async Task A_quorum_queue_is_declared_without_a_word_about_redelivery_counting()
    {
        await Declarer().DeclareConsumeTopologyAsync(_channel);

        _log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_publisher_is_not_warned_about_a_classic_queue_it_neither_declares_nor_consumes()
    {
        var options = TestOptions.Valid();
        options.QueueType = RabbitMQQueueType.Classic;

        await Declarer(options).DeclarePublishTopologyAsync(_channel);

        _log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task The_exchange_can_be_declared_as_a_direct_one_because_every_binding_names_a_whole_key()
    {
        var options = TestOptions.Valid();
        options.ExchangeType = ExchangeType.Direct;

        await Declarer(options).DeclareConsumeTopologyAsync(_channel);

        // The dead-letter exchange stays a topic exchange: its queue is bound on '#', and only a topic
        // exchange reads that as a pattern.
        _channel.DeclaredExchanges.Should().Equal(
            ("orders.events", ExchangeType.Direct),
            ("orders.events.dlx", ExchangeType.Topic));
    }

    [Fact]
    public async Task The_dead_letter_queue_is_bounded_when_the_application_says_how_much_it_keeps()
    {
        var options = TestOptions.Valid();
        options.DeadLetterQueueMessageTtl = TimeSpan.FromDays(14);
        options.DeadLetterQueueMaxLength = 50_000;

        await Declarer(options).DeclareConsumeTopologyAsync(_channel);

        var dlq = _channel.DeclaredQueues.Should().ContainSingle(q => q.Queue == "orders.queue.dlq").Subject;

        dlq.Arguments.Should().ContainKey(RabbitMQTopologyDeclarer.MessageTtlArgument)
            .WhoseValue.Should().Be(TimeSpan.FromDays(14).TotalMilliseconds);
        dlq.Arguments.Should().ContainKey(RabbitMQTopologyDeclarer.MaxLengthArgument)
            .WhoseValue.Should().Be(50_000L);
    }

    [Fact]
    public async Task One_bound_on_the_dead_letter_queue_does_not_drag_the_other_along()
    {
        var options = TestOptions.Valid();
        options.DeadLetterQueueMaxLength = 100;

        await Declarer(options).DeclareConsumeTopologyAsync(_channel);

        var dlq = _channel.DeclaredQueues.Should().ContainSingle(q => q.Queue == "orders.queue.dlq").Subject;

        dlq.Arguments.Should().ContainKey(RabbitMQTopologyDeclarer.MaxLengthArgument);
        dlq.Arguments.Should().NotContainKey(RabbitMQTopologyDeclarer.MessageTtlArgument);
    }

    private RabbitMQTopologyDeclarer Declarer(BsftRabbitMQOptions? options = null) =>
        new(new RabbitMQTopology(options ?? TestOptions.Valid()), _log);

    private sealed class RecordingLogger : ILogger<RabbitMQTopologyDeclarer>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
