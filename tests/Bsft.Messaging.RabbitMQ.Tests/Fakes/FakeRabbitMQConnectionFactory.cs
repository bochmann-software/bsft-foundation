using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.Tests.Support;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>A connection factory that hands out prepared connections, or fails as an outage would.</summary>
internal sealed class FakeRabbitMQConnectionFactory : IRabbitMQConnectionFactory
{
    private readonly Queue<FakeRabbitMQConnection> _connections = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeRabbitMQConnectionFactory"/> class.
    /// </summary>
    /// <param name="signal">
    /// The wakeup to pulse on every call, and to hand to a connection this factory creates itself
    /// (rather than one a test queued through <see cref="WithConnection"/>) so a test waiting on this
    /// factory also wakes up for what that connection does. Defaults to one of its own.
    /// </param>
    public FakeRabbitMQConnectionFactory(ChangeSignal? signal = null) => Signal = signal ?? new ChangeSignal();

    /// <summary>The wakeup pulsed every time this factory is called.</summary>
    public ChangeSignal Signal { get; }

    public int CreateCount { get; private set; }

    public List<FakeRabbitMQConnection> CreatedConnections { get; } = [];

    /// <summary>Set to have connecting fail, as an unreachable broker does.</summary>
    public Exception? CreateFailure { get; set; }

    /// <summary>Set to have connecting wait for this task, as a slow broker does, so a second caller can arrive meanwhile.</summary>
    public Task? Hold { get; set; }

    public FakeRabbitMQConnectionFactory WithConnection(FakeRabbitMQConnection connection)
    {
        _connections.Enqueue(connection);
        return this;
    }

    public Task<IRabbitMQConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        CreateCount++;

        if (CreateFailure is not null)
        {
            Signal.Pulse();
            return Task.FromException<IRabbitMQConnection>(CreateFailure);
        }

        var connection = _connections.Count > 0 ? _connections.Dequeue() : new FakeRabbitMQConnection(Signal);
        CreatedConnections.Add(connection);
        Signal.Pulse();

        return Hold is null ? Task.FromResult<IRabbitMQConnection>(connection) : AfterHoldAsync(connection, Hold);
    }

    private static async Task<IRabbitMQConnection> AfterHoldAsync(FakeRabbitMQConnection connection, Task hold)
    {
        await hold;
        return connection;
    }
}
