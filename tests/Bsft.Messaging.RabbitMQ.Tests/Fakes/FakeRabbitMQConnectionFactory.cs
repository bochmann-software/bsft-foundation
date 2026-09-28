using Bsft.Messaging.RabbitMQ.Connections;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>A connection factory that hands out prepared connections, or fails as an outage would.</summary>
internal sealed class FakeRabbitMQConnectionFactory : IRabbitMQConnectionFactory
{
    private readonly Queue<FakeRabbitMQConnection> _connections = new();

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
            return Task.FromException<IRabbitMQConnection>(CreateFailure);

        var connection = _connections.Count > 0 ? _connections.Dequeue() : new FakeRabbitMQConnection();
        CreatedConnections.Add(connection);

        return Hold is null ? Task.FromResult<IRabbitMQConnection>(connection) : AfterHoldAsync(connection, Hold);
    }

    private static async Task<IRabbitMQConnection> AfterHoldAsync(FakeRabbitMQConnection connection, Task hold)
    {
        await hold;
        return connection;
    }
}
