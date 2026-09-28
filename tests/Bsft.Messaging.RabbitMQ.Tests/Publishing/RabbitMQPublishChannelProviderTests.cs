using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client;
using Xunit;
using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.Publishing;

namespace Bsft.Messaging.RabbitMQ.Tests.Publishing;

public class RabbitMQPublishChannelProviderTests
{
    private readonly FakeRabbitMQConnectionFactory _connectionFactory = new();

    private readonly RabbitMQTopologyDeclarer _declarer = new(
        new RabbitMQTopology(TestOptions.Valid()), NullLogger<RabbitMQTopologyDeclarer>.Instance);

    [Fact]
    public void A_null_dependency_is_rejected()
    {
        var settings = Settings();

        var withoutFactory = () => new RabbitMQPublishChannelProvider(null!, _declarer, settings);
        var withoutDeclarer = () => new RabbitMQPublishChannelProvider(_connectionFactory, null!, settings);
        var withoutSettings = () => new RabbitMQPublishChannelProvider(_connectionFactory, _declarer, null!);

        withoutFactory.Should().Throw<ArgumentNullException>();
        withoutDeclarer.Should().Throw<ArgumentNullException>();
        withoutSettings.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task The_first_call_opens_a_connection_and_declares_the_publish_topology()
    {
        var channel = new FakeRabbitMQChannel();
        _connectionFactory.WithConnection(new FakeRabbitMQConnection().WithChannel(channel));
        await using var provider = Provider();

        var opened = await provider.GetChannelAsync();

        opened.Should().BeSameAs(channel);
        channel.DeclaredExchanges.Should().Equal(("orders.events", ExchangeType.Topic));
    }

    [Fact]
    public async Task The_publishing_channel_is_opened_with_publisher_confirmations()
    {
        var connection = new FakeRabbitMQConnection();
        _connectionFactory.WithConnection(connection);
        await using var provider = Provider();

        await provider.GetChannelAsync();

        connection.RequestedConfirmations.Should().Equal(true);
    }

    [Fact]
    public async Task A_healthy_channel_is_reused_rather_than_reopened()
    {
        await using var provider = Provider();

        var first = await provider.GetChannelAsync();
        var second = await provider.GetChannelAsync();

        second.Should().BeSameAs(first);
        _connectionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task A_closed_channel_is_replaced_on_the_next_publish()
    {
        var closed = new FakeRabbitMQChannel();
        var replacement = new FakeRabbitMQChannel();
        _connectionFactory.WithConnection(
            new FakeRabbitMQConnection().WithChannel(closed).WithChannel(replacement));
        await using var provider = Provider();

        await provider.GetChannelAsync();
        closed.IsOpen = false;
        var second = await provider.GetChannelAsync();

        second.Should().BeSameAs(replacement);
        closed.DisposeCount.Should().Be(1);
        replacement.DeclaredExchanges.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_dropped_connection_is_replaced_together_with_its_channel()
    {
        var dropped = new FakeRabbitMQConnection();
        var replacement = new FakeRabbitMQConnection();
        _connectionFactory.WithConnection(dropped).WithConnection(replacement);
        await using var provider = Provider();

        var first = await provider.GetChannelAsync();
        ((FakeRabbitMQChannel)first).IsOpen = false;
        dropped.IsOpen = false;
        await provider.GetChannelAsync();

        _connectionFactory.CreateCount.Should().Be(2);
        dropped.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task A_channel_whose_declaration_failed_is_closed_again_rather_than_handed_out()
    {
        var channel = new FakeRabbitMQChannel { DeclareFailure = new OperationInterruptedException() };
        _connectionFactory.WithConnection(new FakeRabbitMQConnection().WithChannel(channel));
        await using var provider = Provider();

        var get = () => provider.GetChannelAsync();

        await get.Should().ThrowAsync<OperationInterruptedException>();
        channel.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task An_unreachable_broker_surfaces_as_the_client_failure_for_the_bus_to_translate()
    {
        _connectionFactory.CreateFailure = new BrokerUnreachableException(new InvalidOperationException("down"));
        await using var provider = Provider();

        var get = () => provider.GetChannelAsync();

        await get.Should().ThrowAsync<BrokerUnreachableException>();
    }

    [Fact]
    public async Task Concurrent_callers_share_one_channel_instead_of_each_opening_one()
    {
        await using var provider = Provider();

        var channels = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => provider.GetChannelAsync()));

        channels.Should().AllSatisfy(c => c.Should().BeSameAs(channels[0]));
        _connectionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task Disposal_closes_the_channel_and_the_connection()
    {
        var channel = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(channel);
        _connectionFactory.WithConnection(connection);
        var provider = Provider();
        await provider.GetChannelAsync();

        await provider.DisposeAsync();

        channel.DisposeCount.Should().Be(1);
        connection.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Disposal_is_idempotent()
    {
        var channel = new FakeRabbitMQChannel();
        _connectionFactory.WithConnection(new FakeRabbitMQConnection().WithChannel(channel));
        var provider = Provider();
        await provider.GetChannelAsync();

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        channel.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Disposing_a_provider_that_never_connected_closes_nothing()
    {
        var provider = Provider();

        await provider.DisposeAsync();

        _connectionFactory.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task A_disposed_provider_refuses_to_hand_out_a_channel()
    {
        var provider = Provider();
        await provider.DisposeAsync();

        var get = () => provider.GetChannelAsync();

        await get.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Publishes_are_spread_over_as_many_channels_as_the_application_asked_for()
    {
        // A publish with confirmations waits for the broker on the channel it went out on, so several
        // channels are what stops concurrent publishers waiting behind one another.
        var connection = new FakeRabbitMQConnection();
        _connectionFactory.WithConnection(connection);
        await using var provider = Provider(publishChannelCount: 3);

        var first = await provider.GetChannelAsync();
        var second = await provider.GetChannelAsync();
        var third = await provider.GetChannelAsync();
        var fourth = await provider.GetChannelAsync();

        new[] { first, second, third }.Should().OnlyHaveUniqueItems();
        fourth.Should().BeSameAs(first, "the fourth publish comes round to the first channel again");
        _connectionFactory.CreateCount.Should().Be(1, "the channels share one connection");
    }

    [Fact]
    public async Task Every_channel_of_the_pool_carries_the_declared_publish_topology()
    {
        // A channel handed out without the exchange declared on it would publish into an object nobody
        // has established exists, and which channel a publish gets is not something a caller chooses.
        await using var provider = Provider(publishChannelCount: 2);

        var first = (FakeRabbitMQChannel)await provider.GetChannelAsync();
        var second = (FakeRabbitMQChannel)await provider.GetChannelAsync();

        first.DeclaredExchanges.Should().Equal(("orders.events", ExchangeType.Topic));
        second.DeclaredExchanges.Should().Equal(("orders.events", ExchangeType.Topic));
    }

    [Fact]
    public async Task One_closed_channel_of_the_pool_is_replaced_without_disturbing_the_others()
    {
        await using var provider = Provider(publishChannelCount: 2);
        var first = (FakeRabbitMQChannel)await provider.GetChannelAsync();
        var second = await provider.GetChannelAsync();

        first.IsOpen = false;
        var replacement = await provider.GetChannelAsync();

        replacement.Should().NotBeSameAs(first).And.NotBeSameAs(second);
        first.DisposeCount.Should().Be(1);
        ((FakeRabbitMQChannel)second).DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task A_dropped_connection_takes_every_channel_of_the_pool_with_it()
    {
        // A channel outlives its connection nowhere but in this object's fields, and leaving one there
        // would hand out a channel on a connection that has been disposed.
        var dropped = new FakeRabbitMQConnection();
        _connectionFactory.WithConnection(dropped).WithConnection(new FakeRabbitMQConnection());
        await using var provider = Provider(publishChannelCount: 2);
        var first = (FakeRabbitMQChannel)await provider.GetChannelAsync();
        var second = (FakeRabbitMQChannel)await provider.GetChannelAsync();

        first.IsOpen = false;
        second.IsOpen = false;
        dropped.IsOpen = false;
        await provider.GetChannelAsync();

        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
        _connectionFactory.CreateCount.Should().Be(2);
    }

    [Fact]
    public async Task Disposal_closes_every_channel_of_the_pool()
    {
        var provider = Provider(publishChannelCount: 3);
        var opened = new List<FakeRabbitMQChannel>();

        for (var i = 0; i < 3; i++)
            opened.Add((FakeRabbitMQChannel)await provider.GetChannelAsync());

        await provider.DisposeAsync();

        opened.Should().OnlyContain(c => c.DisposeCount == 1);
    }

    [Fact]
    public async Task Callers_that_find_the_same_slot_empty_at_once_share_the_one_channel_the_first_opens()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new FakeRabbitMQChannel();
        var connection = new FakeRabbitMQConnection().WithChannel(channel);
        _connectionFactory.WithConnection(connection);
        _connectionFactory.Hold = hold.Task;
        await using var provider = Provider();

        var first = provider.GetChannelAsync();
        var second = provider.GetChannelAsync();
        second.IsCompleted.Should().BeFalse("the first caller is still connecting");
        hold.SetResult();

        (await first).Should().BeSameAs(channel);
        (await second).Should().BeSameAs(channel);
        _connectionFactory.CreateCount.Should().Be(1);
        connection.CreatedChannels.Should().ContainSingle();
    }

    private RabbitMQPublishChannelProvider Provider(int publishChannelCount = 1) =>
        new(_connectionFactory, _declarer, Settings(publishChannelCount));

    private static RabbitMQConnectionSettings Settings(int publishChannelCount = 1)
    {
        var options = TestOptions.Valid();
        options.PublishChannelCount = publishChannelCount;

        return new RabbitMQConnectionSettings(options);
    }
}
