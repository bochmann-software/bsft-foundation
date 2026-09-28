using Bsft.Messaging.RabbitMQ.Connections;

namespace Bsft.Messaging.RabbitMQ.Publishing;

/// <summary>
/// Owns the publishing connection and its channels: opens them lazily, declares the publish topology on
/// every channel it creates, hands them out in turn, and replaces a channel the broker has closed.
/// </summary>
/// <remarks>
/// <para>
/// There is no buffering and no retry here. A publish that cannot reach the broker fails, and the
/// outbox — which still holds the message, because it only marks a row processed once the publish
/// returned — tries again on its next pass. A queue of our own in front of the broker's would be a
/// second store of messages that no transaction protects.
/// </para>
/// <para>
/// How many channels there are is a throughput decision and nothing more. A publish with confirmations
/// waits for the broker on the channel it went out on, so concurrent publishes over one channel wait
/// behind each other; over several they do not. Every channel is equivalent — each carries the same
/// declared topology and each confirms on its own — so a caller takes whichever comes next and nothing
/// downstream depends on which one it got.
/// </para>
/// </remarks>
internal sealed class RabbitMQPublishChannelProvider : IRabbitMQPublishChannelProvider
{
    private readonly IRabbitMQConnectionFactory _connectionFactory;
    private readonly IRabbitMQTopologyDeclarer _declarer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IRabbitMQChannel?[] _channels;

    private IRabbitMQConnection? _connection;
    private long _handedOut = -1;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQPublishChannelProvider"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory opening connections to the broker.</param>
    /// <param name="declarer">The declarer creating the publish topology on a new channel.</param>
    /// <param name="settings">The settings, supplying how many channels to spread publishes over.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RabbitMQPublishChannelProvider(
        IRabbitMQConnectionFactory connectionFactory,
        IRabbitMQTopologyDeclarer declarer,
        RabbitMQConnectionSettings settings)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _declarer = declarer ?? throw new ArgumentNullException(nameof(declarer));

        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        _channels = new IRabbitMQChannel?[settings.PublishChannelCount];
    }

    /// <inheritdoc />
    public async Task<IRabbitMQChannel> GetChannelAsync(CancellationToken cancellationToken = default)
    {
        var slot = NextSlot();

        // The overwhelmingly common case is a healthy channel, and reading the slot is enough to know
        // it. Taking the gate for it would serialise every publish behind one another for no reason.
        var current = _channels[slot];
        if (current is not null && current.IsOpen)
            return current;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Re-checked inside the gate: several publishes can find the same slot closed at the same
            // moment, and only the first of them should replace it.
            var established = _channels[slot];
            if (established is not null && established.IsOpen)
                return established;

            await DiscardChannelAsync(slot).ConfigureAwait(false);

            if (_connection is null || !_connection.IsOpen)
            {
                // Every channel belongs to the connection it was opened on, so a connection being
                // replaced takes all of them with it rather than leaving husks in the other slots that
                // each caller would have to discover for itself.
                await DiscardChannelsAsync().ConfigureAwait(false);
                await DiscardConnectionAsync().ConfigureAwait(false);
                _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            var opened = await OpenDeclaredChannelAsync(_connection, cancellationToken).ConfigureAwait(false);
            _channels[slot] = opened;

            return opened;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Picks the next channel slot, round-robin.
    /// </summary>
    /// <remarks>
    /// Cast through <see cref="ulong"/> so that the counter passing <see cref="long.MaxValue"/> keeps
    /// producing slots inside the array instead of a negative index. It also advances on the fast path,
    /// which is the point: an even spread is what makes several channels worth having.
    /// </remarks>
    private int NextSlot() =>
        (int)((ulong)Interlocked.Increment(ref _handedOut) % (ulong)_channels.Length);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        // Marked disposed first, so a publish that arrives from here on is refused instead of opening
        // a connection nobody will close; then the gate is taken, so a publish already inside it
        // finishes against a live channel rather than having it closed underneath.
        _disposed = true;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DiscardChannelsAsync().ConfigureAwait(false);
            await DiscardConnectionAsync().ConfigureAwait(false);
        }
        finally
        {
            // Released but never disposed: a caller may still be queued on it, and disposing a
            // SemaphoreSlim under a waiter throws on that waiter's thread. One whose
            // AvailableWaitHandle was never taken holds nothing that needs releasing.
            _gate.Release();
        }
    }

    /// <summary>
    /// Opens a channel with publisher confirmations and declares the publish topology on it. A channel
    /// whose declaration failed is closed again rather than kept: handing it out would publish into an
    /// exchange nobody has established exists.
    /// </summary>
    private async Task<IRabbitMQChannel> OpenDeclaredChannelAsync(
        IRabbitMQConnection connection, CancellationToken cancellationToken)
    {
        var channel = await connection.CreateChannelAsync(publisherConfirmations: true, cancellationToken).ConfigureAwait(false);
        var declared = false;

        try
        {
            await _declarer.DeclarePublishTopologyAsync(channel, cancellationToken).ConfigureAwait(false);
            declared = true;
        }
        finally
        {
            if (!declared)
                await channel.DisposeAsync().ConfigureAwait(false);
        }

        return channel;
    }

    /// <summary>
    /// Drops the channel in one slot. The slot is cleared before the disposal, so a disposal that fails
    /// on an already-broken channel still leaves this provider ready to open a fresh one.
    /// </summary>
    private async ValueTask DiscardChannelAsync(int slot)
    {
        var channel = _channels[slot];
        _channels[slot] = null;

        if (channel is not null)
            await channel.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Drops every channel, on the same reasoning as <see cref="DiscardChannelAsync"/>.</summary>
    private async ValueTask DiscardChannelsAsync()
    {
        for (var slot = 0; slot < _channels.Length; slot++)
            await DiscardChannelAsync(slot).ConfigureAwait(false);
    }

    /// <summary>Drops the current connection, on the same reasoning as <see cref="DiscardChannelsAsync"/>.</summary>
    private async ValueTask DiscardConnectionAsync()
    {
        var connection = _connection;
        _connection = null;

        if (connection is not null)
            await connection.DisposeAsync().ConfigureAwait(false);
    }
}
