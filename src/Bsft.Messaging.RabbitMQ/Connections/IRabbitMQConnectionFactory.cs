namespace Bsft.Messaging.RabbitMQ.Connections;

/// <summary>
/// Opens connections to the broker. The seam that lets everything above it be exercised without one.
/// </summary>
internal interface IRabbitMQConnectionFactory
{
    /// <summary>Opens a connection to the broker.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The opened connection.</returns>
    Task<IRabbitMQConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}
