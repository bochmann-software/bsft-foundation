namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// Everything the application states about its RabbitMQ transport: where the broker is, what the
/// topology is called, and how the adapter behaves while publishing and consuming.
/// </summary>
/// <remarks>
/// <para>
/// The naming properties are all optional. Left unset they follow the convention described on each of
/// them, derived from <see cref="ApplicationName"/>, so a host that only names itself gets a complete
/// and predictable topology. They exist as overrides for a deployment that has to fit a naming scheme
/// it does not own — an exchange another team already operates, for instance.
/// </para>
/// <para>
/// <see cref="UserName"/> and <see cref="Password"/> have no defaults on purpose. The adapter refuses
/// to start without them rather than falling back to the broker's well-known development account, so a
/// host that forgot to bind its configuration fails at startup instead of connecting as whoever the
/// default happens to be. Bind them from configuration or a secret store; they do not belong in code.
/// </para>
/// </remarks>
public sealed class BsftRabbitMQOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:RabbitMQ";

    private readonly List<string> _subscribedEventNames = [];

    /// <summary>
    /// Gets or sets the name of this application. It identifies the connection on the broker and is the
    /// stem every unset name below is derived from, so it is required.
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>Gets or sets the broker host name. Defaults to <c>localhost</c>.</summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>Gets or sets the broker port. Defaults to the AMQP port 5672.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>Gets or sets the virtual host to connect to. Defaults to <c>/</c>.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Gets or sets the user to connect as. Required; there is no default.</summary>
    public string? UserName { get; set; }

    /// <summary>Gets or sets the password of that user. Required; there is no default.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets whether the connection is made over TLS. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Turning it on does not move <see cref="Port"/>. AMQPS listens on 5671 rather than 5672 and the
    /// port is stated, not inferred, so a deployment that binds the port from configuration is not
    /// overruled by this flag. Set both.
    /// </para>
    /// <para>
    /// Everything else about the TLS handshake — which protocol versions are offered, which certificate
    /// authorities are trusted — is left to the client library and therefore to the host's own .NET and
    /// operating-system trust configuration. That is where an operator manages it for every other
    /// outbound connection the process makes, and duplicating those knobs here would mean a second place
    /// to get them wrong.
    /// </para>
    /// </remarks>
    public bool UseTls { get; set; }

    /// <summary>
    /// Gets or sets the host name the broker's certificate is validated against. Defaults to
    /// <see langword="null"/>, which uses <see cref="HostName"/>.
    /// </summary>
    /// <remarks>
    /// It exists for the deployment that reaches the broker under a name its certificate does not carry
    /// — through a load balancer, a service mesh, or an IP address in <see cref="HostName"/>. Setting it
    /// selects which name is verified; it never switches verification off.
    /// </remarks>
    public string? TlsServerName { get; set; }

    /// <summary>
    /// Gets or sets how long the client waits between attempts to re-establish a dropped connection.
    /// Defaults to five seconds.
    /// </summary>
    public TimeSpan NetworkRecoveryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the interval the client and the broker exchange heartbeats at. Defaults to 60
    /// seconds, which is the client's own default.
    /// </summary>
    /// <remarks>
    /// It is what detects a connection that died without either side being told — a firewall dropping an
    /// idle NAT entry is the usual one. The broker closes the connection after two missed intervals, so
    /// this is roughly half the time a consumer can be subscribed to a socket that no longer exists.
    /// Lower it where an idle connection is reaped aggressively; the negotiated value is the smaller of
    /// this and the broker's own setting.
    /// </remarks>
    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets how long one of the client's own protocol calls may take before it is abandoned.
    /// Defaults to 20 seconds, which is the client's own default.
    /// </summary>
    /// <remarks>
    /// It bounds a single RPC with the broker — declaring a queue, opening a channel, acknowledging a
    /// delivery — and running past it is raised as a plain <see cref="TimeoutException"/>, which this
    /// adapter treats as a broker that stopped answering. It is not the publisher confirmation deadline;
    /// that is <see cref="PublisherConfirmTimeout"/>.
    /// </remarks>
    public TimeSpan ContinuationTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets or sets the topic exchange this application publishes to. Defaults to
    /// <c>{ApplicationName}.events</c>.
    /// </summary>
    /// <remarks>
    /// One exchange per application is the convention, so an event's origin is readable from the
    /// exchange it arrived on. A host that consumes another application's events overrides this with
    /// that application's exchange name.
    /// </remarks>
    public string? ExchangeName { get; set; }

    /// <summary>
    /// Gets or sets the AMQP type the application's exchange is declared as. Defaults to
    /// <c>topic</c>. Only <c>topic</c> and <c>direct</c> are accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The routing key is the event name and a queue is bound once per name passed to
    /// <see cref="Subscribe"/>, which both exchange types route by. <c>topic</c> is the default because
    /// it is the one that leaves room: a consumer can widen a binding to a family of names with a
    /// pattern later without the producer changing anything. <c>direct</c> matches the whole key and
    /// nothing else, which is all this adapter's own bindings ask for.
    /// </para>
    /// <para>
    /// <c>fanout</c> and <c>headers</c> are rejected rather than passed through, because neither routes
    /// on the routing key: a fanout exchange would deliver every event to every bound queue, and a
    /// headers exchange would deliver none of them. Either way the subscriptions declared here would
    /// silently stop meaning what they say.
    /// </para>
    /// <para>
    /// The dead-letter exchange is always a topic exchange, whatever this is set to. Its queue is bound
    /// on <c>#</c> so that nothing dead-lettered is dropped for want of a matching binding, and <c>#</c>
    /// is a pattern only a topic exchange reads as one.
    /// </para>
    /// <para>
    /// An exchange's type is fixed when it is declared. Pointing this adapter at an exchange that
    /// already exists under the other type fails the declaration with <c>PRECONDITION_FAILED</c> rather
    /// than converting it, exactly as <see cref="QueueType"/> does.
    /// </para>
    /// </remarks>
    public string ExchangeType { get; set; } = global::RabbitMQ.Client.ExchangeType.Topic;

    /// <summary>
    /// Gets or sets the exchange undeliverable messages are routed to. Defaults to
    /// <c>{ExchangeName}.dlx</c>.
    /// </summary>
    public string? DeadLetterExchangeName { get; set; }

    /// <summary>
    /// Gets or sets this application's queue. Defaults to <c>{ApplicationName}.queue</c>.
    /// </summary>
    /// <remarks>
    /// The queue belongs to the consumer, not to the event: one queue per consuming application, bound
    /// once per event name it subscribes to. Every instance of the application consumes from that one
    /// queue, so the instances share the load instead of each receiving every message.
    /// </remarks>
    public string? QueueName { get; set; }

    /// <summary>
    /// Gets or sets the queue the dead-letter exchange delivers to. Defaults to
    /// <c>{QueueName}.dlq</c>.
    /// </summary>
    public string? DeadLetterQueueName { get; set; }

    /// <summary>
    /// Gets or sets how long a message may sit in the dead-letter queue before the broker discards it.
    /// Defaults to <see langword="null"/>, which lets it stay indefinitely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dead-letter queue nobody drains grows until the broker's disk alarm stops the whole node,
    /// including the exchanges this application publishes to — so a bound is worth setting, and worth
    /// setting well above the time anyone would take to look at a parked message. What it discards is
    /// the last copy of an event, which is why there is no default.
    /// </para>
    /// <para>
    /// It maps to the queue's <c>x-message-ttl</c> argument, and a queue's arguments are fixed when it
    /// is declared. Changing this against a broker where the dead-letter queue already exists fails the
    /// declaration with <c>PRECONDITION_FAILED</c> — the same inequivalent-argument refusal
    /// <see cref="QueueType"/> runs into — so applying it means deleting the queue and letting the next
    /// start re-declare it, after moving anything still in it.
    /// </para>
    /// </remarks>
    public TimeSpan? DeadLetterQueueMessageTtl { get; set; }

    /// <summary>
    /// Gets or sets how many messages the dead-letter queue holds before the broker drops the oldest.
    /// Defaults to <see langword="null"/>, which is unbounded.
    /// </summary>
    /// <remarks>
    /// It maps to the queue's <c>x-max-length</c> argument and carries the same two caveats as
    /// <see cref="DeadLetterQueueMessageTtl"/>: what it drops is the last copy of an event, and changing
    /// it on a broker that already has the queue fails the declaration until the queue is deleted and
    /// re-declared. A full queue drops from the head, so the oldest parked message goes first.
    /// </remarks>
    public long? DeadLetterQueueMaxLength { get; set; }

    /// <summary>Gets the event names this application's queue is bound to.</summary>
    public IReadOnlyList<string> SubscribedEventNames => _subscribedEventNames;

    /// <summary>
    /// Gets or sets the kind of queue this application's queue is declared as. Defaults to
    /// <see cref="RabbitMQQueueType.Quorum"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is quorum because of what it carries rather than what it replicates: a quorum queue
    /// stamps <c>x-delivery-count</c> on a redelivery, which is where the adapter reads
    /// <c>IMessageDelivery.DeliveryAttempt</c> from. On a classic queue that count does not exist, every
    /// delivery is reported as the first, and the consumer host's <c>MaxDeliveryAttempts</c> is never
    /// reached — a message whose handler always fails is then requeued forever instead of being parked.
    /// </para>
    /// <para>
    /// A queue's type is fixed when it is declared. Pointing this adapter at a queue that already exists
    /// under the other type fails the declaration with <c>PRECONDITION_FAILED</c> rather than converting
    /// it, so switching means declaring a new queue and draining the old one.
    /// </para>
    /// </remarks>
    public RabbitMQQueueType QueueType { get; set; } = RabbitMQQueueType.Quorum;

    /// <summary>
    /// Gets or sets how many unacknowledged messages the broker may have outstanding per consumer.
    /// Defaults to 20. It is the only backpressure the consuming side has: without it the broker pushes
    /// the whole queue at a single consumer, which then holds all of it in memory.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 20;

    /// <summary>
    /// Gets or sets how many channels the publishing side opens and spreads its publishes over.
    /// Defaults to 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A publish with confirmations waits for the broker on the channel it went out on, and a channel
    /// is one conversation: two publishes on the same channel wait one behind the other. One channel is
    /// enough for the outbox, which dispatches strictly sequentially anyway, so the default keeps the
    /// broker's channel table as small as this application can make it.
    /// </para>
    /// <para>
    /// Raise it where something publishes concurrently: parking runs on the consumer loop and shares
    /// this side, so a host that both publishes and consumes has two publishers as soon as it parks
    /// anything, as does an application calling <c>IMessageBus</c> from several requests at once.
    /// Publishes are handed out round-robin, so raising it only helps that far — a single sequential
    /// publisher uses one channel at a time whatever this says.
    /// </para>
    /// <para>
    /// It does not change ordering guarantees, because there are none to change: the outbox decides the
    /// order events are published in, and RabbitMQ only preserves it per channel.
    /// </para>
    /// </remarks>
    public int PublishChannelCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets how long a publish waits for the broker's confirmation before it is treated as
    /// failed. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// A publish that never returns would hold an outbox lease until it expires and stall the whole
    /// dispatch pass behind it, so waiting forever is not an option. Timing out reports a failure the
    /// outbox retries — at the risk of a duplicate, should the confirmation only be late, which
    /// at-least-once delivery permits anyway.
    /// </remarks>
    public TimeSpan PublisherConfirmTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the content type published messages are labelled with, and the one consumed
    /// messages are checked against. Defaults to <c>application/json</c>, which is the encoding message
    /// version 1 defines, so change it only together with a custom <c>IIntegrationEventSerializer</c>
    /// that produces something else.
    /// </summary>
    /// <remarks>
    /// It is a statement about what this application's serializer reads and writes, so it is honoured in
    /// both directions: it is stamped onto every published message, and a delivery that states a
    /// different one is refused rather than fed to a serializer that cannot read it. A delivery that
    /// states none at all is accepted — a producer that labels nothing is not a producer that labels
    /// something else — and the parameters after the media type, a <c>charset</c> in particular, are
    /// ignored on both sides of the comparison.
    /// </remarks>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Declares that this application's queue is bound to an event name, so the broker routes messages
    /// published under that name to it. Declaring the same name twice is ignored.
    /// </summary>
    /// <param name="eventName">The stable event name, as registered with <c>BsftMessagingOptions.AddEvent</c>.</param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="eventName"/> is null or whitespace.</exception>
    public BsftRabbitMQOptions Subscribe(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("An integration event name is required.", nameof(eventName));

        if (!_subscribedEventNames.Contains(eventName, StringComparer.Ordinal))
            _subscribedEventNames.Add(eventName);

        return this;
    }
}
