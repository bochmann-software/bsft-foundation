using Bsft.Messaging.Outbox;
using Bsft.Outbox.Postgres;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// Background service that keeps a listening session on the outbox notification channel and drains the
/// outbox whenever a commit announces itself.
/// </summary>
/// <remarks>
/// <para>
/// It adds nothing to the delivery guarantee and deliberately so. The drain it triggers is the
/// dispatcher's own <see cref="IOutboxProcessor.ProcessOnceAsync"/>, so every message still goes
/// through the same lease, the same retry schedule and the same dead-letter policy, and two instances
/// woken by the same notification share the backlog exactly as two polling instances do. What this
/// buys is latency: the gap between a commit and its delivery stops being the poll interval.
/// </para>
/// <para>
/// The polling dispatcher stays registered and keeps running, which is the fallback. A notification
/// that is never sent, never received, or lost with the connection it was travelling on costs the poll
/// interval and nothing else — so a host may lengthen that interval once this is wired, but it should
/// not switch it off.
/// </para>
/// <para>
/// Reconnecting is the <see cref="PostgresListenLoop"/> of <c>Bsft.Outbox.Postgres</c>, which the
/// wake-up of every outbox shares: a session that ends is a pass that finished, and the loop waits
/// <see cref="BsftMessagingPostgresOptions.ReconnectDelay"/> and starts another.
/// </para>
/// </remarks>
internal sealed class OutboxNotificationWorker : BackgroundService
{
    private readonly IOutboxProcessor _processor;
    private readonly IOutboxNotificationChannel _channel;
    private readonly BsftMessagingPostgresOptions _options;
    private readonly IPostgresListenLoop _loop;
    private readonly ILogger<OutboxNotificationWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxNotificationWorker"/> class.
    /// </summary>
    /// <param name="channel">The listening session this worker keeps alive.</param>
    /// <param name="processor">The dispatcher pass a notification triggers.</param>
    /// <param name="options">The options carrying the reconnect delay.</param>
    /// <param name="loop">The reconnecting loop the sessions run in.</param>
    /// <param name="logger">Logger used to report sessions that ended.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public OutboxNotificationWorker(
        IOutboxNotificationChannel channel,
        IOutboxProcessor processor,
        IOptions<BsftMessagingPostgresOptions> options,
        IPostgresListenLoop loop,
        ILogger<OutboxNotificationWorker> logger)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _loop = loop ?? throw new ArgumentNullException(nameof(loop));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the listen-and-reconnect loop until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal Task RunLoopAsync(CancellationToken stoppingToken = default) =>
        _loop.RunAsync(_channel.ListenAsync, DrainAsync, _options.ReconnectDelay, _logger, stoppingToken);

    /// <summary>
    /// Drains the outbox until a pass delivers nothing.
    /// </summary>
    /// <remarks>
    /// A pass claims at most one batch, so one notification behind a hundred committed messages would
    /// otherwise deliver a batch and leave the rest to the poll this package exists to get ahead of.
    /// The loop ends on the count rather than on a fixed number of passes: a pass that delivered
    /// nothing is the outbox saying it is empty or that another instance has claimed what is left, and
    /// both are reasons to stop.
    /// </remarks>
    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (await _processor.ProcessOnceAsync(cancellationToken).ConfigureAwait(false) > 0)
        {
            // The pass itself is the work; the condition is the whole loop.
        }
    }
}
