namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// The validated connection and behaviour settings of the transport: where the broker is, who the
/// adapter connects as, and the two numbers — prefetch and confirm timeout — that decide how much the
/// adapter has outstanding at any moment.
/// </summary>
/// <remarks>
/// It is built at registration so that a missing password or a nonsensical port fails the host at
/// startup rather than on the first publish, which in a background dispatcher would only surface as a
/// retried outbox message. The credentials are held here because the client needs them to connect;
/// nothing in this package logs an instance of this type, and it deliberately does not override
/// <see cref="object.ToString"/>, so it cannot be interpolated into a log line by accident.
/// </remarks>
internal sealed class RabbitMQConnectionSettings
{
    /// <summary>The highest TCP port number.</summary>
    private const int MaxPort = 65535;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQConnectionSettings"/> class from the
    /// application's options.
    /// </summary>
    /// <param name="options">The options to read the settings from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the application name, host name, virtual host, credentials or content type are
    /// missing.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the port is outside the TCP range, the prefetch count or the publish channel count is
    /// below one, or the confirm timeout, recovery interval, heartbeat or continuation timeout is not
    /// positive.
    /// </exception>
    public RabbitMQConnectionSettings(BsftRabbitMQOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        ApplicationName = Required(options.ApplicationName, nameof(BsftRabbitMQOptions.ApplicationName));
        HostName = Required(options.HostName, nameof(BsftRabbitMQOptions.HostName));
        VirtualHost = Required(options.VirtualHost, nameof(BsftRabbitMQOptions.VirtualHost));
        ContentType = Required(options.ContentType, nameof(BsftRabbitMQOptions.ContentType));

        UseTls = options.UseTls;

        // The name the certificate is checked against falls back to the host being connected to, which
        // is what verifying a certificate means when nobody said otherwise. It is resolved here rather
        // than at connection time so that the fallback is visible in one place instead of being a null
        // check inside the factory.
        TlsServerName = string.IsNullOrWhiteSpace(options.TlsServerName) ? HostName : options.TlsServerName;

        // Named separately from the rest: the message has to say that these are configured, never
        // defaulted, because the alternative — silently connecting as the broker's development account —
        // is the failure this check exists to prevent.
        UserName = Credential(options.UserName, nameof(BsftRabbitMQOptions.UserName));
        Password = Credential(options.Password, nameof(BsftRabbitMQOptions.Password));

        Port = InRange(options.Port);
        PrefetchCount = Prefetch(options.PrefetchCount);
        PublishChannelCount = AtLeastOne(
            options.PublishChannelCount, nameof(BsftRabbitMQOptions.PublishChannelCount));
        PublisherConfirmTimeout = Positive(
            options.PublisherConfirmTimeout, nameof(BsftRabbitMQOptions.PublisherConfirmTimeout));
        NetworkRecoveryInterval = Positive(
            options.NetworkRecoveryInterval, nameof(BsftRabbitMQOptions.NetworkRecoveryInterval));
        Heartbeat = Positive(options.Heartbeat, nameof(BsftRabbitMQOptions.Heartbeat));
        ContinuationTimeout = Positive(
            options.ContinuationTimeout, nameof(BsftRabbitMQOptions.ContinuationTimeout));
    }

    /// <summary>Gets the application name the connection identifies itself with on the broker.</summary>
    public string ApplicationName { get; }

    /// <summary>Gets the broker host name.</summary>
    public string HostName { get; }

    /// <summary>Gets the broker port.</summary>
    public int Port { get; }

    /// <summary>Gets the virtual host to connect to.</summary>
    public string VirtualHost { get; }

    /// <summary>Gets the user to connect as.</summary>
    public string UserName { get; }

    /// <summary>Gets the password of that user.</summary>
    public string Password { get; }

    /// <summary>Gets whether the connection is made over TLS.</summary>
    public bool UseTls { get; }

    /// <summary>
    /// Gets the host name the broker's certificate is validated against, which is
    /// <see cref="HostName"/> unless a different one was stated.
    /// </summary>
    public string TlsServerName { get; }

    /// <summary>Gets the pause between attempts to re-establish a dropped connection.</summary>
    public TimeSpan NetworkRecoveryInterval { get; }

    /// <summary>Gets the interval the client and the broker exchange heartbeats at.</summary>
    public TimeSpan Heartbeat { get; }

    /// <summary>Gets how long one of the client's own protocol calls may take.</summary>
    public TimeSpan ContinuationTimeout { get; }

    /// <summary>Gets how many channels the publishing side spreads its publishes over.</summary>
    public int PublishChannelCount { get; }

    /// <summary>Gets how many unacknowledged messages the broker may have outstanding per consumer.</summary>
    public ushort PrefetchCount { get; }

    /// <summary>Gets how long a publish waits for the broker's confirmation.</summary>
    public TimeSpan PublisherConfirmTimeout { get; }

    /// <summary>Gets the content type published messages are labelled with.</summary>
    public string ContentType { get; }

    private static string Required(string value, string propertyName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{propertyName} is required.", nameof(BsftRabbitMQOptions))
            : value;

    private static string Credential(string? value, string propertyName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                $"{propertyName} is required. The RabbitMQ transport has no credential default on purpose; bind it from configuration or a secret store.",
                nameof(BsftRabbitMQOptions))
            : value;

    private static int InRange(int port) =>
        port is < 1 or > MaxPort
            ? throw new ArgumentOutOfRangeException(
                nameof(BsftRabbitMQOptions.Port), port, $"The port must be between 1 and {MaxPort}.")
            : port;

    /// <summary>
    /// Rejects a prefetch of zero. AMQP reads it as "no limit", so the broker would push the entire
    /// queue at one consumer — the opposite of what setting a prefetch is for, and quietly enough that
    /// it is only noticed when a backlog exhausts the consumer's memory.
    /// </summary>
    private static ushort Prefetch(ushort prefetchCount) =>
        prefetchCount == 0
            ? throw new ArgumentOutOfRangeException(
                nameof(BsftRabbitMQOptions.PrefetchCount),
                prefetchCount,
                "The prefetch count must be at least 1; AMQP reads zero as an unlimited prefetch.")
            : prefetchCount;

    private static int AtLeastOne(int value, string propertyName) =>
        value < 1
            ? throw new ArgumentOutOfRangeException(propertyName, value, $"{propertyName} must be at least 1.")
            : value;

    private static TimeSpan Positive(TimeSpan value, string propertyName) =>
        value <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(propertyName, value, $"{propertyName} must be positive.")
            : value;
}
