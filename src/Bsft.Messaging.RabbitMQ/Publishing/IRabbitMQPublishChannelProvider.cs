namespace Bsft.Messaging.RabbitMQ.Publishing;

/// <summary>
/// Hands out the channel the bus publishes on, opening the connection on first use and replacing the
/// channel once it is no longer usable.
/// </summary>
/// <remarks>
/// A channel is not thread-safe and opening one is not free, so the publishing side keeps a single one
/// and shares it. This is where its lifetime lives, which keeps the bus itself about publishing rather
/// than about connection management.
/// </remarks>
internal interface IRabbitMQPublishChannelProvider : IAsyncDisposable
{
    /// <summary>
    /// Returns an open channel with the publish topology declared on it, creating one if the current
    /// channel is missing or has been closed.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>An open channel.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    Task<IRabbitMQChannel> GetChannelAsync(CancellationToken cancellationToken = default);
}
