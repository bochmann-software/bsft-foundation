using Bsft.Messaging.Consuming;
using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.DependencyInjection;
using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests;

/// <summary>
/// What happens when the stream does not survive: the source lets the failure out, and the consumer
/// host — not the adapter — is what waits and subscribes again.
/// </summary>
public class RabbitMQSeamReconnectTests
{

    private readonly ChangeSignal _signal = new();
    private readonly FakeRabbitMQConnectionFactory _connectionFactory;
    private readonly FakeRabbitMQConnection _connection;
    private readonly FakeRabbitMQChannel _channel;

    public RabbitMQSeamReconnectTests()
    {
        _connectionFactory = new FakeRabbitMQConnectionFactory(_signal);
        _connection = new FakeRabbitMQConnection(_signal);
        _channel = new FakeRabbitMQChannel(_signal);
    }

    [Fact]
    public async Task A_stream_that_could_not_connect_is_enumerated_again_after_the_restart_delay()
    {
        // The first enumeration throws before it yields anything, which is what the host treats as a
        // failed subscription: it logs it, waits out ReceiveRestartDelay and asks the source again.
        _connection.WithChannel(_channel);
        _connectionFactory.WithConnection(_connection);
        _connectionFactory.CreateFailure = new BrokerUnreachableException(new IOException("down"));

        await using var provider = Provider();
        var host = provider.GetServices<IHostedService>().OfType<IntegrationEventConsumerHost>().Single();
        await host.StartAsync(CancellationToken.None);

        await Eventually.HoldsAsync(() => _connectionFactory.CreateCount >= 1, _signal);
        _connectionFactory.CreateFailure = null;

        await Eventually.HoldsAsync(() => _channel.ConsumedQueue is not null, _signal);
        _connectionFactory.CreateCount.Should().BeGreaterThan(1);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_recovery_whose_re_subscription_fails_ends_the_stream_so_the_host_subscribes_again()
    {
        // The client recovers the connection and this adapter puts the subscription back on it. When
        // that fails — the broker refuses the re-declared topology, or the RPC times out — the recovered
        // connection is healthy, so no further recovery is ever reported and nothing would ask again.
        // The stream therefore has to end, because ending it is what reaches the host's restart loop.
        _connection.WithChannel(_channel);
        _connectionFactory.WithConnection(_connection);

        await using var provider = Provider();
        var host = provider.GetServices<IHostedService>().OfType<IntegrationEventConsumerHost>().Single();
        await host.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _channel.ConsumedQueue is not null, _signal);

        _connection.CreateChannelFailure = new OperationInterruptedException();
        await _connection.RaiseRecoverySucceededAsync();

        // A second connection, still refusing nothing: the source stopped the consumer as the stream
        // unwound, so the host's next enumeration opens the subscription from scratch.
        await Eventually.HoldsAsync(() => _connectionFactory.CreatedConnections.Count >= 2, _signal);
        var reconnected = _connectionFactory.CreatedConnections[1];
        await Eventually.HoldsAsync(() => reconnected.CreatedChannels.Any(c => c.ConsumedQueue is not null), _signal);

        _connection.DisposeCount.Should().Be(1);
        _connection.HasRecoverySubscriber.Should().BeFalse();

        await host.StopAsync(CancellationToken.None);
    }

    private ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRabbitMQConnectionFactory>(_connectionFactory);

        services.AddBsftRabbitMQMessaging(o =>
        {
            o.ApplicationName = "orders";
            o.UserName = TestOptions.UserName;
            o.Password = TestOptions.Password;
            o.Subscribe(TestDeliveries.EventName);
        });

        services.AddBsftMessaging(o => o.AddEvent<SeamOrderPlacedEvent>(TestDeliveries.EventName));
        services.AddBsftConsumerHost(o =>
        {
            o.ConsumerName = "orders";
            o.ReceiveRestartDelay = TimeSpan.FromMilliseconds(20);
        });

        return services.BuildServiceProvider();
    }

}
