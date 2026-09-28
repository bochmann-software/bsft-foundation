namespace Bsft.Messaging.RabbitMQ.Connections;

/// <summary>
/// The slice of an AMQP connection this adapter uses: opening channels, knowing whether the connection
/// is alive, and being told when the client has re-established it after an outage.
/// </summary>
internal interface IRabbitMQConnection : IAsyncDisposable
{
    /// <summary>Gets whether the connection is usable.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Gets or sets what runs after the client has re-established the connection following an outage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A recovered connection brings its channels back but not what this adapter put on them: the
    /// topology and the consumer are declared by us and are gone with the old channel. This is where a
    /// consumer learns that it has to subscribe again, and it is why the client's own topology recovery
    /// is switched off — two components restoring the same topology would mean two places deciding
    /// what it is.
    /// </para>
    /// <para>
    /// A settable handler rather than an event, because the restoration has exactly one owner: the
    /// consumer that opened this connection. An event would let a second subscriber declare the
    /// topology in parallel with the first, and the work it returns has to be awaited — which a
    /// multicast invocation cannot do for more than the last handler it reaches.
    /// </para>
    /// </remarks>
    Func<Task>? RecoverySucceeded { get; set; }

    /// <summary>Opens a channel on this connection.</summary>
    /// <param name="publisherConfirmations">
    /// Whether the channel tracks publisher confirmations, so that a publish completes only once the
    /// broker has confirmed the message. A consuming channel does not need them.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The opened channel.</returns>
    Task<IRabbitMQChannel> CreateChannelAsync(bool publisherConfirmations, CancellationToken cancellationToken = default);
}
