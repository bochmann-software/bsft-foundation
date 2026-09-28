// Aliased because this type has a property of the same name, which would otherwise shadow the client's
// class of AMQP exchange-type names wherever the two meet.
using AmqpExchangeType = RabbitMQ.Client.ExchangeType;

namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// The resolved shape of the broker objects this application uses: the exchange it publishes to, its
/// own queue, the dead-letter pair behind them, and the arguments each is declared with. Built once from
/// <see cref="BsftRabbitMQOptions"/> at registration, so the convention is applied and checked before the
/// first connection is opened.
/// </summary>
/// <remarks>
/// It exists so that exactly one component decides what the topology is called. The declarer, the
/// publisher and the consumer all read the names from here rather than each re-deriving them, which is
/// what makes "the queue the consumer reads is the queue the declarer created" true by construction
/// instead of by two matching pieces of string concatenation.
/// </remarks>
internal sealed class RabbitMQTopology
{
    /// <summary>The longest name AMQP 0-9-1 allows for an exchange or a queue.</summary>
    private const int MaxNameLength = 255;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQTopology"/> class from the application's
    /// options, applying the naming convention wherever an explicit name was not given.
    /// </summary>
    /// <param name="options">The options to resolve the names from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a name resolves to nothing, exceeds the length AMQP allows, collides with the
    /// dead-letter object it is paired with, or when the exchange type is one that does not route on the
    /// routing key.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a dead-letter queue bound is set to something that is not positive.
    /// </exception>
    public RabbitMQTopology(BsftRabbitMQOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        ExchangeName = Resolve(options.ExchangeName, options.ApplicationName, ".events", nameof(BsftRabbitMQOptions.ExchangeName));
        DeadLetterExchangeName = Resolve(
            options.DeadLetterExchangeName, ExchangeName, ".dlx", nameof(BsftRabbitMQOptions.DeadLetterExchangeName));
        QueueName = Resolve(options.QueueName, options.ApplicationName, ".queue", nameof(BsftRabbitMQOptions.QueueName));
        DeadLetterQueueName = Resolve(
            options.DeadLetterQueueName, QueueName, ".dlq", nameof(BsftRabbitMQOptions.DeadLetterQueueName));

        // A dead-letter object that is the object it protects turns every rejected message into an
        // immediate redelivery of itself, which is a loop the broker will happily run forever.
        RejectCollision(ExchangeName, DeadLetterExchangeName, nameof(BsftRabbitMQOptions.DeadLetterExchangeName));
        RejectCollision(QueueName, DeadLetterQueueName, nameof(BsftRabbitMQOptions.DeadLetterQueueName));

        ExchangeType = RoutableExchangeType(options.ExchangeType);
        QueueType = options.QueueType;
        DeadLetterQueueMessageTtl = PositiveOrNone(
            options.DeadLetterQueueMessageTtl, nameof(BsftRabbitMQOptions.DeadLetterQueueMessageTtl));
        DeadLetterQueueMaxLength = PositiveOrNone(
            options.DeadLetterQueueMaxLength, nameof(BsftRabbitMQOptions.DeadLetterQueueMaxLength));

        // Copied rather than referenced: the options object stays in the caller's hands, and a
        // subscription added to it after registration would otherwise change a topology that has
        // already been declared.
        SubscribedEventNames = [.. options.SubscribedEventNames];
    }

    /// <summary>Gets the exchange integration events are published to.</summary>
    public string ExchangeName { get; }

    /// <summary>Gets the exchange a rejected message is dead-lettered to.</summary>
    public string DeadLetterExchangeName { get; }

    /// <summary>Gets this application's queue.</summary>
    public string QueueName { get; }

    /// <summary>Gets the queue behind the dead-letter exchange.</summary>
    public string DeadLetterQueueName { get; }

    /// <summary>Gets the AMQP type <see cref="ExchangeName"/> is declared as.</summary>
    public string ExchangeType { get; }

    /// <summary>Gets the kind of queue <see cref="QueueName"/> is declared as.</summary>
    public RabbitMQQueueType QueueType { get; }

    /// <summary>
    /// Gets how long a message may sit in <see cref="DeadLetterQueueName"/>, or
    /// <see langword="null"/> when it may stay indefinitely.
    /// </summary>
    public TimeSpan? DeadLetterQueueMessageTtl { get; }

    /// <summary>
    /// Gets how many messages <see cref="DeadLetterQueueName"/> holds, or <see langword="null"/> when
    /// it is unbounded.
    /// </summary>
    public long? DeadLetterQueueMaxLength { get; }

    /// <summary>Gets the event names <see cref="QueueName"/> is bound to, in declaration order.</summary>
    public IReadOnlyList<string> SubscribedEventNames { get; }

    /// <summary>
    /// Returns the explicit name when one was given, and otherwise the stem with the conventional
    /// suffix appended.
    /// </summary>
    private static string Resolve(string? explicitName, string stem, string suffix, string propertyName)
    {
        var name = string.IsNullOrWhiteSpace(explicitName) ? $"{stem}{suffix}" : explicitName;

        if (string.IsNullOrWhiteSpace(stem) && string.IsNullOrWhiteSpace(explicitName))
            throw new ArgumentException(
                $"The name for {propertyName} cannot be derived because {nameof(BsftRabbitMQOptions.ApplicationName)} is empty. Set the application name, or set {propertyName} explicitly.",
                nameof(BsftRabbitMQOptions));

        if (name.Length > MaxNameLength)
            throw new ArgumentException(
                $"The name '{name}' for {propertyName} is {name.Length} characters long; AMQP allows at most {MaxNameLength}.",
                nameof(BsftRabbitMQOptions));

        return name;
    }

    /// <summary>
    /// Returns the exchange type when it is one this adapter's routing scheme survives.
    /// </summary>
    /// <remarks>
    /// The routing key is the event name and every binding names one, so only the two types that route
    /// by routing key are accepted. A fanout exchange ignores the key and would deliver every event to
    /// every bound queue; a headers exchange routes on arguments these bindings do not carry and would
    /// deliver none. Both would leave the subscriptions declared here silently meaning something else.
    /// </remarks>
    private static string RoutableExchangeType(string exchangeType)
    {
        if (string.IsNullOrWhiteSpace(exchangeType))
            throw new ArgumentException(
                $"{nameof(BsftRabbitMQOptions.ExchangeType)} is required.", nameof(BsftRabbitMQOptions));

        return exchangeType is AmqpExchangeType.Topic or AmqpExchangeType.Direct
            ? exchangeType
            : throw new ArgumentException(
                $"{nameof(BsftRabbitMQOptions.ExchangeType)} is '{exchangeType}'. Only '{AmqpExchangeType.Topic}' and '{AmqpExchangeType.Direct}' route on the routing key, which is where this adapter puts the event name; '{AmqpExchangeType.Fanout}' would deliver every event to every subscribed queue and '{AmqpExchangeType.Headers}' would deliver none.",
                nameof(BsftRabbitMQOptions));
    }

    /// <summary>
    /// Accepts a dead-letter bound that is either absent or meaningful. Zero and below are refused
    /// rather than passed on: the broker reads a zero TTL as "expire on arrival" and a zero length as a
    /// queue that holds nothing, so a bound somebody left at its default would quietly discard every
    /// message parking exists to keep.
    /// </summary>
    private static TimeSpan? PositiveOrNone(TimeSpan? value, string propertyName) =>
        value is { } bound && bound <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(propertyName, bound, $"{propertyName} must be positive when it is set.")
            : value;

    /// <inheritdoc cref="PositiveOrNone(TimeSpan?, string)"/>
    private static long? PositiveOrNone(long? value, string propertyName) =>
        value is { } bound && bound <= 0
            ? throw new ArgumentOutOfRangeException(propertyName, bound, $"{propertyName} must be positive when it is set.")
            : value;

    /// <summary>Rejects a dead-letter name that is the very name it is supposed to catch for.</summary>
    private static void RejectCollision(string name, string deadLetterName, string propertyName)
    {
        if (string.Equals(name, deadLetterName, StringComparison.Ordinal))
            throw new ArgumentException(
                $"{propertyName} is '{deadLetterName}', which is the object it dead-letters for. A rejected message would be routed straight back to where it was rejected.",
                nameof(BsftRabbitMQOptions));
    }

}
