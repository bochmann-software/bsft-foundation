using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Bsft.Messaging.RabbitMQ.Connections;

/// <summary>
/// Declares the exchanges, queues and bindings named by <see cref="RabbitMQTopology"/>.
/// </summary>
/// <remarks>
/// <para>
/// The routing key is the event name, so a consumer binds to the events it wants by name. The exchange
/// is a topic exchange by default, which lets a consumer widen that to a family of names with a pattern
/// later without the producer changing anything.
/// </para>
/// <para>
/// Everything is declared durable. A message the outbox has already marked processed exists nowhere
/// else, so a queue that a broker restart removes takes events with it that no retry will produce
/// again.
/// </para>
/// </remarks>
internal sealed class RabbitMQTopologyDeclarer : IRabbitMQTopologyDeclarer
{
    /// <summary>The queue argument naming the exchange a rejected message is routed to.</summary>
    internal const string DeadLetterExchangeArgument = "x-dead-letter-exchange";

    /// <summary>The queue argument choosing the queue's kind.</summary>
    internal const string QueueTypeArgument = "x-queue-type";

    /// <summary>The queue argument bounding how long a message may sit in the queue, in milliseconds.</summary>
    internal const string MessageTtlArgument = "x-message-ttl";

    /// <summary>The queue argument bounding how many messages the queue holds.</summary>
    internal const string MaxLengthArgument = "x-max-length";

    /// <summary>The value <see cref="QueueTypeArgument"/> takes for a quorum queue.</summary>
    internal const string QuorumQueueType = "quorum";

    /// <summary>The value <see cref="QueueTypeArgument"/> takes for a classic queue.</summary>
    internal const string ClassicQueueType = "classic";

    /// <summary>The topic pattern that matches every routing key.</summary>
    private const string MatchAllRoutingKeys = "#";

    private readonly RabbitMQTopology _topology;
    private readonly ILogger<RabbitMQTopologyDeclarer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQTopologyDeclarer"/> class.
    /// </summary>
    /// <param name="topology">The names to declare.</param>
    /// <param name="logger">The logger used to report a queue the attempt budget cannot work on.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RabbitMQTopologyDeclarer(RabbitMQTopology topology, ILogger<RabbitMQTopologyDeclarer> logger)
    {
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task DeclarePublishTopologyAsync(IRabbitMQChannel channel, CancellationToken cancellationToken = default)
    {
        if (channel is null)
            throw new ArgumentNullException(nameof(channel));

        return channel.DeclareExchangeAsync(_topology.ExchangeName, _topology.ExchangeType, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeclareConsumeTopologyAsync(IRabbitMQChannel channel, CancellationToken cancellationToken = default)
    {
        if (channel is null)
            throw new ArgumentNullException(nameof(channel));

        ReportUncountedRedeliveries();

        // The consumer needs the very exchange the publisher declares, so it asks for it through the
        // same method rather than repeating the declaration. Two copies of it would be two places to
        // change the exchange type in, and only one of them would be noticed.
        await DeclarePublishTopologyAsync(channel, cancellationToken).ConfigureAwait(false);

        // The dead-letter exchange is a topic exchange whatever the application's own exchange is
        // declared as. Its queue is bound on '#' so that nothing dead-lettered is dropped for want of a
        // matching binding, and '#' is a pattern only a topic exchange reads as one.
        await channel
            .DeclareExchangeAsync(_topology.DeadLetterExchangeName, ExchangeType.Topic, cancellationToken)
            .ConfigureAwait(false);

        // The dead-letter queue is bound to every key, because a dead-lettered message keeps the
        // routing key it was published under. Binding it by event name instead would drop exactly the
        // messages whose name nobody thought to list — which is the set most worth keeping.
        //
        // It takes no queue type of its own: nothing redelivers from it, so the one thing the type
        // decides here — whether the broker counts deliveries — has nothing to decide. Leaving it at
        // the broker's default also keeps it declarable on a node where quorum queues are unavailable.
        await channel
            .DeclareQueueAsync(_topology.DeadLetterQueueName, DeadLetterQueueArguments(), cancellationToken)
            .ConfigureAwait(false);
        await channel
            .BindQueueAsync(_topology.DeadLetterQueueName, _topology.DeadLetterExchangeName, MatchAllRoutingKeys, cancellationToken)
            .ConfigureAwait(false);

        // No x-dead-letter-routing-key: leaving it out keeps the original routing key on the rejected
        // message, so the dead-letter queue still says which event each entry was.
        //
        // The queue type is stated rather than left to the broker's default, and it defaults to quorum,
        // because it decides whether the consumer host's attempt budget can work at all: only a quorum
        // queue stamps x-delivery-count, which is where DeliveryAttempt is read from. On a classic
        // queue every delivery reports as the first one and a failing message is requeued forever.
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [DeadLetterExchangeArgument] = _topology.DeadLetterExchangeName,
            [QueueTypeArgument] = ArgumentValueOf(_topology.QueueType),
        };

        await channel.DeclareQueueAsync(_topology.QueueName, arguments, cancellationToken).ConfigureAwait(false);

        foreach (var eventName in _topology.SubscribedEventNames)
        {
            await channel
                .BindQueueAsync(_topology.QueueName, _topology.ExchangeName, eventName, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Says out loud, once per subscription, that a classic queue leaves the consumer host's attempt
    /// budget with nothing to count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a warning rather than a refusal because a classic queue is a legitimate deployment. A node
    /// without quorum queues has no alternative, and a host that bounds redelivery with a broker-side
    /// policy — or one whose handlers cannot fail in a way retrying would not fix — is not relying on
    /// the budget in the first place. Refusing to start would take those deployments down for a setting
    /// they had chosen deliberately.
    /// </para>
    /// <para>
    /// It is said at all because the failure is otherwise invisible: nothing throws, nothing is dropped,
    /// and a message whose handler always fails is simply requeued forever while
    /// <c>ConsumerOptions.MaxDeliveryAttempts</c> reads as configured and never triggers. That is not a
    /// state anyone would think to look for.
    /// </para>
    /// <para>
    /// Only the consuming side says it. A publisher declares no queue and has no attempt budget, so the
    /// warning would be noise in every producer-only host.
    /// </para>
    /// </remarks>
    private void ReportUncountedRedeliveries()
    {
        if (_topology.QueueType != RabbitMQQueueType.Classic)
            return;

        _logger.LogWarning(
            "Queue {QueueName} is declared as a classic queue, which does not stamp {DeliveryCountHeader}. Every delivery therefore reports as the first attempt, ConsumerOptions.MaxDeliveryAttempts is never reached, and a message whose handler always fails is requeued indefinitely instead of being parked. Bound redelivery with a broker-side policy, or declare a quorum queue.",
            _topology.QueueName,
            RabbitMQMessageMapper.DeliveryCountHeader);
    }

    /// <summary>
    /// Returns the arguments the dead-letter queue is declared with, or <see langword="null"/> when it
    /// is left entirely at the broker's defaults.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty dictionary when nothing is bounded, because that is what every earlier
    /// version of this adapter declared the queue with, and a queue's arguments are compared for
    /// equivalence on re-declaration: sending an empty set where none was sent before would be a
    /// <c>PRECONDITION_FAILED</c> against a dead-letter queue that already exists.
    /// </remarks>
    private IDictionary<string, object?>? DeadLetterQueueArguments()
    {
        if (_topology.DeadLetterQueueMessageTtl is null && _topology.DeadLetterQueueMaxLength is null)
            return null;

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Both are whole milliseconds and whole messages as far as AMQP is concerned, and both are
        // written as the signed 64-bit integers the protocol's field table carries them in.
        if (_topology.DeadLetterQueueMessageTtl is { } ttl)
            arguments[MessageTtlArgument] = (long)ttl.TotalMilliseconds;

        if (_topology.DeadLetterQueueMaxLength is { } maxLength)
            arguments[MaxLengthArgument] = maxLength;

        return arguments;
    }

    /// <summary>Returns the AMQP value for a queue kind.</summary>
    private static string ArgumentValueOf(RabbitMQQueueType queueType) =>
        queueType == RabbitMQQueueType.Classic ? ClassicQueueType : QuorumQueueType;
}
