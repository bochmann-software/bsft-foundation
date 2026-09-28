using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Diagnostics.CodeAnalysis;

namespace Bsft.Messaging.RabbitMQ.Connections;

/// <summary>
/// Forwards <see cref="IRabbitMQConnection"/> onto the client's <see cref="IConnection"/> and
/// translates its recovery notification into the one this adapter listens for. Excluded from coverage
/// for the same reason as <see cref="RabbitMQChannelAdapter"/>: it is delegation that needs a broker.
/// </summary>
[ExcludeFromCodeCoverage] // pure delegation to RabbitMQ.Client; unreachable without a live broker
internal sealed class RabbitMQConnectionAdapter : IRabbitMQConnection
{
    private readonly IConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQConnectionAdapter"/> class.
    /// </summary>
    /// <param name="connection">The connection to forward to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connection"/> is null.</exception>
    public RabbitMQConnectionAdapter(IConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _connection.RecoverySucceededAsync += OnRecoverySucceededAsync;
    }

    /// <inheritdoc />
    public Func<Task>? RecoverySucceeded { get; set; }

    /// <inheritdoc />
    public bool IsOpen => _connection.IsOpen;

    /// <inheritdoc />
    public async Task<IRabbitMQChannel> CreateChannelAsync(
        bool publisherConfirmations, CancellationToken cancellationToken = default)
    {
        // Tracking is enabled together with confirmations, never on its own: without it the client
        // acknowledges the publish as soon as the frame is written and the returned task says nothing
        // about whether the broker took the message.
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: publisherConfirmations,
            publisherConfirmationTrackingEnabled: publisherConfirmations);

        var channel = await _connection.CreateChannelAsync(options, cancellationToken).ConfigureAwait(false);

        return new RabbitMQChannelAdapter(channel);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _connection.RecoverySucceededAsync -= OnRecoverySucceededAsync;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private Task OnRecoverySucceededAsync(object sender, AsyncEventArgs arguments)
    {
        var handler = RecoverySucceeded;

        return handler is null ? Task.CompletedTask : handler();
    }
}
