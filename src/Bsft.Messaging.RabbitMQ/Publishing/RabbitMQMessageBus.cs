using Bsft.Messaging.Publishing;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;

namespace Bsft.Messaging.RabbitMQ.Publishing;

/// <summary>
/// The RabbitMQ <see cref="IMessageBus"/>: it publishes the wire envelope to the application's topic
/// exchange under the event name and returns only once the broker has confirmed the message.
/// </summary>
/// <remarks>
/// <para>
/// The confirmation is what makes the outbox honest. <c>OutboxDispatcher</c> marks a row processed as
/// soon as <c>PublishAsync</c> returns, so a publish that returned when the bytes were merely written
/// would let the row be forgotten while the broker still had every chance to drop the message. A
/// publish here returns when the broker has taken responsibility for it, and a rejection or a
/// confirmation that never arrives is raised as a <see cref="MessagePublishException"/> — a failed
/// attempt the outbox records and retries.
/// </para>
/// <para>
/// It publishes and does no more. It does not consume, and it does not queue anything of its own while
/// the broker is unreachable: the outbox is already a durable store of undelivered messages, and a
/// second one in front of it would hold events that no transaction protects.
/// </para>
/// </remarks>
internal sealed class RabbitMQMessageBus : IMessageBus
{
    private readonly IRabbitMQPublishChannelProvider _channelProvider;
    private readonly IRabbitMQMessageMapper _mapper;
    private readonly RabbitMQTopology _topology;
    private readonly RabbitMQConnectionSettings _settings;
    private readonly ILogger<RabbitMQMessageBus> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageBus"/> class.
    /// </summary>
    /// <param name="channelProvider">The provider of the publishing channel.</param>
    /// <param name="mapper">The mapper turning the envelope into AMQP properties and a body.</param>
    /// <param name="topology">The resolved names, supplying the exchange to publish to.</param>
    /// <param name="settings">The settings, supplying the confirm timeout.</param>
    /// <param name="logger">The logger used to report publishes and their failures.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RabbitMQMessageBus(
        IRabbitMQPublishChannelProvider channelProvider,
        IRabbitMQMessageMapper mapper,
        RabbitMQTopology topology,
        RabbitMQConnectionSettings settings,
        ILogger<RabbitMQMessageBus> logger)
    {
        _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// Thrown when the message has no event name. The name is the routing key, so a message without one
    /// would be published to an exchange that cannot route it anywhere.
    /// </exception>
    public async Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        if (string.IsNullOrWhiteSpace(message.EventName))
            throw new ArgumentException(
                $"Integration event {message.EventId} has no event name, so there is no routing key to publish it under.",
                nameof(message));

        var properties = _mapper.ToProperties(message);
        var body = _mapper.ToBody(message);
        var channel = await OpenChannelAsync(message, cancellationToken).ConfigureAwait(false);

        // Only the publish runs under the confirm window. Opening a connection above has timeouts of
        // its own, and folding it in here would report a broker that is merely slow to connect as a
        // confirmation that never came.
        using var confirmWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        confirmWindow.CancelAfter(_settings.PublisherConfirmTimeout);

        try
        {
            await channel
                .PublishAsync(_topology.ExchangeName, message.EventName, properties, body, confirmWindow.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException cancellation) when (!cancellationToken.IsCancellationRequested)
        {
            // The window closed rather than the caller withdrawing the publish. Whether the broker
            // never confirmed or merely confirmed late cannot be told apart from here, so it counts as
            // a failure: the outbox retrying it costs a possible duplicate, which at-least-once
            // delivery allows, while treating it as delivered would cost the event.
            throw Failed(message, $"the broker did not confirm it within {_settings.PublisherConfirmTimeout}", cancellation);
        }
        catch (IOException unreachable)
        {
            // The socket can die while the confirmation is outstanding, and it surfaces as an
            // IOException rather than as a protocol exception, exactly as it does while the channel is
            // opened a few lines below.
            throw Failed(message, "the broker could not be reached", unreachable);
        }
        catch (RabbitMQClientException rejection)
        {
            // Covers the broker refusing the message outright — a nack, or a channel it closed under
            // the publish — as well as every other protocol-level failure.
            throw Failed(message, "the broker rejected it", rejection);
        }
        catch (TimeoutException unresponsive)
        {
            // The client raises a plain TimeoutException when one of its own RPCs runs past the
            // continuation timeout, which is neither a RabbitMQClientException nor an IOException. It
            // has to be translated here too, or the one failure this class exists to classify would
            // reach the outbox as a raw transport exception.
            throw Failed(message, "the broker did not respond in time", unresponsive);
        }

        _logger.LogDebug(
            "Published integration event {EventId} as {EventName} to exchange {Exchange}.",
            message.EventId,
            message.EventName,
            _topology.ExchangeName);
    }

    /// <summary>
    /// Gets the publishing channel, reporting an unreachable broker as a publish failure rather than as
    /// whatever transport exception the client raised.
    /// </summary>
    private async Task<IRabbitMQChannel> OpenChannelAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await _channelProvider.GetChannelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RabbitMQClientException unreachable)
        {
            throw Failed(message, "the broker could not be reached", unreachable);
        }
        catch (IOException unreachable)
        {
            // BrokerUnreachableException is an IOException rather than a RabbitMQClientException, and a
            // socket that dies mid-handshake surfaces as one too.
            throw Failed(message, "the broker could not be reached", unreachable);
        }
        catch (TimeoutException unresponsive)
        {
            // Opening a channel and declaring the exchange are RPCs, and a broker that accepts the
            // connection but never answers them times out as a plain TimeoutException.
            throw Failed(message, "the broker did not respond in time", unresponsive);
        }
        catch (ObjectDisposedException shuttingDown)
        {
            // The provider is disposed by a shutdown that reaches it before the outbox dispatcher, and
            // it says so in its own contract. The row this publish came from is still there, so the
            // failure belongs to the outbox rather than to whoever is unwinding the host.
            throw Failed(message, "the broker could not be reached", shuttingDown);
        }
    }

    private MessagePublishException Failed(IntegrationEventMessage message, string reason, Exception cause)
    {
        _logger.LogWarning(
            cause,
            "Publishing integration event {EventId} as {EventName} to exchange {Exchange} failed because {Reason}.",
            message.EventId,
            message.EventName,
            _topology.ExchangeName,
            reason);

        return new MessagePublishException(
            $"Publishing integration event {message.EventId} as '{message.EventName}' failed because {reason}.",
            cause);
    }
}
