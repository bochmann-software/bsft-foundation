using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Outbox.Postgres;

/// <summary>
/// The hosted half of the PostgreSQL wake-up of one outbox: it keeps a listening session on the outbox's
/// notification channel and raises the signal for every notification, and once for every session it
/// starts.
/// </summary>
/// <remarks>
/// <para>
/// It works for any outbox table, because it knows nothing about the table: whoever writes rows
/// announces them on the channel (a trigger, or <c>pg_notify</c> from the application), and this side
/// only listens. It adds nothing to the delivery guarantee. The drain worker's poll interval stays
/// behind it, so a notification that is never sent, never received or lost with its connection costs
/// that interval and nothing else.
/// </para>
/// <para>
/// Every new session raises the signal once as soon as its subscription is in place: rows written while
/// nothing was listening, the first start and every reconnect included, are claimed by a drain that does
/// not wait for the next write. The reconnecting is the shared <see cref="PostgresListenLoop"/>.
/// </para>
/// </remarks>
internal sealed class PostgresOutboxWakeWorker : BackgroundService
{
    private readonly IPostgresListenSession _session;
    private readonly BsftOutboxPostgresOptions _options;
    private readonly PostgresNotificationChannel _channel;
    private readonly PostgresOutboxWakeSignal _signal;
    private readonly IPostgresListenLoop _loop;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="PostgresOutboxWakeWorker"/> class.</summary>
    /// <param name="session">The listening session this worker keeps alive.</param>
    /// <param name="options">The channel, the connection and the reconnect delay.</param>
    /// <param name="signal">The signal raised for every notification.</param>
    /// <param name="loop">The reconnecting loop the session runs in.</param>
    /// <param name="logger">Logger used to report sessions that ended.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the configured channel is not a valid channel name.</exception>
    public PostgresOutboxWakeWorker(
        IPostgresListenSession session,
        IOptions<BsftOutboxPostgresOptions> options,
        PostgresOutboxWakeSignal signal,
        IPostgresListenLoop loop,
        ILogger logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _loop = loop ?? throw new ArgumentNullException(nameof(loop));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channel = new PostgresNotificationChannel(_options.NotificationChannel);
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the listen-and-reconnect loop until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal Task RunLoopAsync(CancellationToken stoppingToken = default) =>
        _loop.RunAsync(
            (onNotified, token) => _session.ListenAsync(_options.ConnectionString, _channel, onNotified, token),
            _signal.RaiseAsync,
            _options.ReconnectDelay,
            _logger,
            stoppingToken);
}
