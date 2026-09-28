using RabbitMQ.Client;
using System.Diagnostics.CodeAnalysis;

namespace Bsft.Messaging.RabbitMQ.Connections;

/// <summary>
/// Builds the client's <see cref="ConnectionFactory"/> from the validated settings and opens the
/// connection. Excluded from coverage: it configures and calls the client, and nothing else.
/// </summary>
[ExcludeFromCodeCoverage] // pure delegation to RabbitMQ.Client; unreachable without a live broker
internal sealed class RabbitMQConnectionFactoryAdapter : IRabbitMQConnectionFactory
{
    private readonly RabbitMQConnectionSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQConnectionFactoryAdapter"/> class.
    /// </summary>
    /// <param name="settings">The validated connection settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    public RabbitMQConnectionFactoryAdapter(RabbitMQConnectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public async Task<IRabbitMQConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.HostName,
            Port = _settings.Port,
            VirtualHost = _settings.VirtualHost,
            UserName = _settings.UserName,
            Password = _settings.Password,

            // The connection identifies the application on the broker's connection list, which is what
            // an operator reads when deciding whose consumer is behind a growing queue.
            ClientProvidedName = _settings.ApplicationName,

            // The client reconnects and brings its channels back on its own. Topology recovery is off
            // because this adapter declares the topology and re-subscribes its consumer itself; leaving
            // both on would mean two components restoring the same objects, and only one of them
            // reading the application's options.
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = false,
            NetworkRecoveryInterval = _settings.NetworkRecoveryInterval,

            // What detects a socket that died without either side being told, and what bounds a single
            // RPC with the broker. Both are stated rather than left at the client's defaults so that the
            // values the options document are the values in force, whichever client version is resolved.
            RequestedHeartbeat = _settings.Heartbeat,
            ContinuationTimeout = _settings.ContinuationTimeout,

            // The protocol versions and the trusted authorities are deliberately left to the client and
            // therefore to the host's own .NET and OS trust configuration, which is where an operator
            // manages them for every other outbound connection this process makes.
            Ssl = new SslOption
            {
                Enabled = _settings.UseTls,
                ServerName = _settings.TlsServerName,
            },
        };

        var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        return new RabbitMQConnectionAdapter(connection);
    }
}
