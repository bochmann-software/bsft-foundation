using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.Consuming;

/// <summary>
/// Background service that consumes integration events: it enumerates the <see cref="IMessageSource"/>
/// the transport provides and hands every delivery to the <see cref="IIntegrationEventConsumer"/>,
/// which decodes it, runs the handlers behind the inbox and completes it toward the transport.
/// </summary>
/// <remarks>
/// <para>
/// It is the receiving counterpart of <see cref="Outbox.OutboxDispatcher"/> and is built to the same rule: a
/// long-running service must not be taken down by anything one message, one handler or one broker
/// hiccup did. A failed stream is logged in full and re-subscribed to after
/// <see cref="ConsumerOptions.ReceiveRestartDelay"/>; only a cancellation of its own stopping token
/// ends the loop.
/// </para>
/// <para>
/// One message is consumed at a time. That is the backpressure the transport sees, and it is what
/// makes the inbox's guarantees describable: within one host, two deliveries of the same event can
/// never be in flight at once.
/// </para>
/// </remarks>
public sealed class IntegrationEventConsumerHost : BackgroundService
{
    private readonly IMessageSource _source;
    private readonly IIntegrationEventConsumer _consumer;
    private readonly ConsumerOptions _options;
    private readonly ILogger<IntegrationEventConsumerHost> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrationEventConsumerHost"/> class.
    /// </summary>
    /// <param name="source">The transport's receiving seam.</param>
    /// <param name="consumer">The consumer handling one delivery at a time.</param>
    /// <param name="options">The messaging options carrying the consumer group.</param>
    /// <param name="logger">Logger used to report stream failures.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the consumer name is absent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the attempt limit, the inbox ledger capacity or the parked message capacity is below
    /// one, the restart delay is not positive, the retry delay is negative, or the inbox retention is
    /// set and not positive.
    /// </exception>
    public IntegrationEventConsumerHost(
        IMessageSource source,
        IIntegrationEventConsumer consumer,
        IOptions<BsftMessagingOptions> options,
        ILogger<IntegrationEventConsumerHost> logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value.Consumer);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Rejects options that would make the consumer misbehave rather than fail. The whole of
    /// <see cref="ConsumerOptions"/> is checked here, because the host is the one component that must
    /// exist for any of the consuming pipeline to run, and startup is the only moment at which these
    /// mistakes are cheap to notice.
    /// </summary>
    /// <remarks>
    /// An absent consumer name is the worst of them: it is half of the inbox key, so an empty one
    /// silently merges the inbox records of every consumer that also left it empty. An attempt limit
    /// below one would park every message on its first delivery, before any handler ever ran. A
    /// non-positive restart delay turns a source that cannot connect into a tight reconnect loop, or
    /// makes the delay itself throw. An inbox ledger capacity below one would leave the in-memory
    /// ledger evicting every record it takes, so no redelivery is ever recognised as one. A parked
    /// message capacity below one would drop every parked message the loopback was asked to keep, which
    /// is the one outcome that has nothing else holding it. A negative retry delay makes the delay
    /// itself throw on the first failed message, and an inbox retention of zero or less would expire
    /// every record the moment it was written, turning the durable inbox into no inbox at all.
    /// </remarks>
    private static ConsumerOptions Validate(ConsumerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConsumerName))
            throw new ArgumentException(
                "The consumer name must be set; it identifies this consumer in the inbox and has no safe default.",
                nameof(ConsumerOptions.ConsumerName));
        if (options.MaxDeliveryAttempts < 1)
            throw new ArgumentOutOfRangeException(
                nameof(ConsumerOptions.MaxDeliveryAttempts),
                options.MaxDeliveryAttempts,
                "The maximum number of delivery attempts must be at least 1.");
        if (options.ReceiveRestartDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(ConsumerOptions.ReceiveRestartDelay),
                options.ReceiveRestartDelay,
                "The receive restart delay must be positive.");
        if (options.InboxLedgerCapacity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(ConsumerOptions.InboxLedgerCapacity),
                options.InboxLedgerCapacity,
                "The inbox ledger capacity must be at least 1.");
        if (options.ParkedMessageCapacity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(ConsumerOptions.ParkedMessageCapacity),
                options.ParkedMessageCapacity,
                "The parked message capacity must be at least 1.");
        if (options.RetryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(ConsumerOptions.RetryDelay),
                options.RetryDelay,
                "The retry delay must not be negative.");
        if (options.InboxRetention is { } retention && retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(ConsumerOptions.InboxRetention),
                retention,
                "The inbox retention must be positive when it is set.");

        return options;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the consume loop until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal async Task RunLoopAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var subscription = ReceiveAllAsync(stoppingToken);

                // The outcome of a subscription is observed rather than caught: a broker that dropped
                // the connection, or a transport that raised on its own, must not take the service
                // down. The failure is logged in full, and the messages it left behind were never
                // completed, so the transport still has them.
                await Task.WhenAny(subscription).ConfigureAwait(false);

                if (subscription.IsFaulted)
                {
                    _logger.LogError(subscription.Exception!.Flatten(), "The integration event message source failed.");
                    await Task.Delay(_options.ReceiveRestartDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // A subscription that ends cancelled while nobody asked the host to stop was cancelled
                // by a token of its own — a broker client's own timeout, typically. Reading that as
                // shutdown would leave the host running with nothing consuming, so it is treated as a
                // failed subscription and re-established after the delay.
                if (subscription.IsCanceled && !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(
                        "The integration event message source was canceled although the consumer was not asked to stop.");
                    await Task.Delay(_options.ReceiveRestartDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Surfaces cancellation as itself. Otherwise the stream simply ended, which a source is
                // allowed to do — it is how a transport signals that it wants to be re-subscribed to.
                await subscription.ConfigureAwait(false);

                await Task.Delay(_options.ReceiveRestartDelay, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected when the stopping token is cancelled inside the stream, a delivery or a delay on
            // shutdown. The filter is what keeps that meaning: without it any cancellation from
            // anywhere below would end the loop as a shutdown, and one handler timeout would stop the
            // consumer for good while the host kept running.
            _logger.LogDebug("Integration event consumer stopping.");
        }
    }

    /// <summary>Consumes the source's stream until it ends or the token is cancelled.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the stream has ended.</returns>
    internal async Task ReceiveAllAsync(CancellationToken cancellationToken = default)
    {
        // Awaiting inside this method rather than returning the source's enumeration is what keeps a
        // source that validates or connects before its first yield — and so throws synchronously —
        // inside the failure handling above instead of unwinding the whole loop.
        await foreach (var delivery in _source.ReceiveAsync(cancellationToken).ConfigureAwait(false))
        {
            await _consumer.ConsumeAsync(delivery, cancellationToken).ConfigureAwait(false);
        }
    }
}
