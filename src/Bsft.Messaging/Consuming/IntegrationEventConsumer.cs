using Bsft.Messaging.Diagnostics;
using Bsft.Messaging.Inbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.Consuming;

/// <summary>
/// The default <see cref="IIntegrationEventConsumer"/>: it decodes a delivery, hands it to the
/// inbox-guarded handlers and completes it toward the transport.
/// </summary>
/// <remarks>
/// <para>
/// It is where the three transport outcomes are decided. A message that was handled or recognised as a
/// redelivery is acknowledged. A message whose handlers failed is handed back — with the wait the
/// retry schedule computed for it, which the transport is asked to honour rather than the consumer
/// sleeping it off — until it has used up its delivery attempts, then parked. A message that cannot be
/// decoded at all is parked at once, without spending attempts on it, because nothing about a
/// redelivery of the same bytes would go differently.
/// </para>
/// <para>
/// Nothing is ever dropped. Every path out of <see cref="ConsumeAsync"/> either completes the delivery
/// or leaves it uncompleted for redelivery, and every park is logged at error level with the reason
/// that was handed to the transport with it.
/// </para>
/// </remarks>
internal sealed class IntegrationEventConsumer : IIntegrationEventConsumer
{
    private readonly IIncomingMessageDecoder _decoder;
    private readonly IInboxGuardedDelivery _delivery;
    private readonly IConsumerTelemetry _telemetry;
    private readonly IMessageRetrySchedule _retrySchedule;
    private readonly ConsumerOptions _options;
    private readonly ILogger<IntegrationEventConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrationEventConsumer"/> class.
    /// </summary>
    /// <param name="decoder">The decoder turning a received message into its event.</param>
    /// <param name="delivery">The inbox-guarded delivery running the handlers.</param>
    /// <param name="telemetry">The telemetry recording what became of each message.</param>
    /// <param name="retrySchedule">Decides how long a failed message is held back before redelivery.</param>
    /// <param name="options">The messaging options carrying the attempt limit.</param>
    /// <param name="logger">Logger used to report retries and parked messages.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="ConsumerOptions.RetryDelay"/> is negative or
    /// <see cref="ConsumerOptions.MaxRetryDelay"/> is not positive.
    /// </exception>
    /// <remarks>
    /// The two retry bounds are re-checked here although the options validator already rejects them at
    /// startup: they are read on the first message that fails, which is the one path that exists to keep
    /// a failure from escaping, and a value that throws there would defeat it.
    /// </remarks>
    public IntegrationEventConsumer(
        IIncomingMessageDecoder decoder,
        IInboxGuardedDelivery delivery,
        IConsumerTelemetry telemetry,
        IMessageRetrySchedule retrySchedule,
        IOptions<BsftMessagingOptions> options,
        ILogger<IntegrationEventConsumer> logger)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _retrySchedule = retrySchedule ?? throw new ArgumentNullException(nameof(retrySchedule));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value.Consumer;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.RetryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options), _options.RetryDelay, "The retry delay must not be negative.");
        if (_options.MaxRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options), _options.MaxRetryDelay, "The retry ceiling must be positive.");
    }

    /// <inheritdoc />
    public async Task ConsumeAsync(IMessageDelivery delivery, CancellationToken cancellationToken = default)
    {
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));

        var message = delivery.Message;

        using var span = _telemetry.BeginDelivery(message.EventName);
        _telemetry.MessageReceived(message.EventName);

        var decoding = DecodeAsync(message);
        await Task.WhenAny(decoding).ConfigureAwait(false);

        if (decoding.IsFaulted)
        {
            await RecordDecodeFailureAsync(delivery, decoding.Exception!.Flatten(), cancellationToken).ConfigureAwait(false);
            return;
        }

        var integrationEvent = await decoding.ConfigureAwait(false);

        var handling = _delivery.DeliverAsync(message, integrationEvent, cancellationToken);

        // The outcome is observed rather than caught, for the same reason the outbox dispatcher does
        // it: a message whose handlers failed has to be handed back to the transport rather than
        // unwind the consumer, and a store or handler failure of any type must reach that decision.
        await Task.WhenAny(handling).ConfigureAwait(false);

        var failure = FailureOf(handling, message, cancellationToken);

        if (failure is not null)
        {
            await RecordFailureAsync(delivery, failure, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Surfaces a shutdown cancellation as itself, leaving the delivery uncompleted so the
        // transport redelivers it; otherwise yields the outcome the inbox decided.
        var outcome = await handling.ConfigureAwait(false);

        if (outcome == EventDeliveryOutcome.Duplicate)
        {
            _telemetry.DuplicateDropped(message.EventName);
            _logger.LogDebug(
                "Integration event {EventId} of type {EventName} was already processed by consumer {ConsumerName}; the redelivery is acknowledged without handling.",
                message.EventId,
                message.EventName,
                _options.ConsumerName);
        }
        else
        {
            _telemetry.MessageHandled(message.EventName);
        }

        await delivery.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Decodes through a task rather than in line, so that a decoder throwing before it returns is
    /// observed here instead of unwinding the consumer. It is the same reason the outbox dispatcher
    /// awaits the bus's publish rather than returning its task.
    /// </summary>
    private async Task<IIntegrationEvent> DecodeAsync(IntegrationEventMessage message)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        return _decoder.Decode(message);
    }

    /// <summary>
    /// Decides what a failed decode costs the message. A <see cref="MessageDecodeException"/> is the
    /// decoder saying the message is permanently unhandleable here, so it is parked at once; anything
    /// else is a decoder that failed in a way it does not describe — a bug, or an implementation
    /// raising outside its contract — and is treated as an ordinary failed delivery, which retries it
    /// and parks it once its attempts run out. Neither path lets the message go uncompleted.
    /// </summary>
    private async Task RecordDecodeFailureAsync(
        IMessageDelivery delivery, AggregateException failure, CancellationToken cancellationToken)
    {
        var refusal = failure.InnerExceptions.Count == 1
            ? failure.InnerExceptions[0] as MessageDecodeException
            : null;

        if (refusal is not null)
        {
            await ParkAsync(delivery, refusal.ParkReason, refusal.Message, refusal, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RecordFailureAsync(delivery, failure, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the outcome of a finished delivery: the failure it has to be tried again for, or
    /// <see langword="null"/> when it succeeded or the consumer itself is stopping.
    /// </summary>
    /// <remarks>
    /// A delivery that ends cancelled while nobody asked the consumer to stop was cancelled by a token
    /// of a handler's own — an HTTP client timeout is the usual one. Reading that as a shutdown would
    /// let it unwind through the host's loop and stop consumption altogether, so one handler timeout
    /// would take the consumer down for as long as the host lives.
    /// </remarks>
    private static AggregateException? FailureOf(Task handling, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        if (handling.IsFaulted)
            return handling.Exception!.Flatten();

        if (handling.IsCanceled && !cancellationToken.IsCancellationRequested)
            return new AggregateException(new TaskCanceledException(
                $"Handling the message was canceled by a token of its own rather than by a shutdown of the consumer; event {message.EventId} of type {message.EventName} was not handled."));

        return null;
    }

    /// <summary>
    /// Hands a failed message back for another attempt, or parks it once it has used up its attempts.
    /// </summary>
    private async Task RecordFailureAsync(
        IMessageDelivery delivery, AggregateException failure, CancellationToken cancellationToken)
    {
        var message = delivery.Message;

        // A single failure is described by its own message: the aggregate's text only says that
        // something went wrong, which is exactly what the recorded reason must not say.
        var detail = failure.InnerExceptions.Count == 1 ? failure.InnerExceptions[0].Message : failure.Message;

        if (delivery.DeliveryAttempt >= _options.MaxDeliveryAttempts)
        {
            await ParkAsync(delivery, MessageParkReason.AttemptsExhausted, detail, failure, cancellationToken).ConfigureAwait(false);
            return;
        }

        _telemetry.DeliveryRetried(message.EventName);

        // The delay is computed and handed on rather than slept off here. The consumer holds one
        // message at a time, so a wait taken in this method is a wait for every message behind it —
        // which is precisely what a throttled recipient must not be able to cause. What the delay costs
        // is now the transport's business: the loopback schedules the redelivery and holds nothing, a
        // transport with no scheduling of its own documents what it does instead.
        var delay = _retrySchedule.NextDelay(
            delivery.DeliveryAttempt,
            _options.RetryDelay,
            _options.MaxRetryDelay,
            _retrySchedule.RequestedRetryAfter(failure));

        _logger.LogError(
            failure,
            "Handling integration event {EventId} of type {EventName} failed on delivery attempt {AttemptNumber} of {MaxDeliveryAttempts}; the message is handed back to the transport for redelivery in {RetryDelay}.",
            message.EventId,
            message.EventName,
            delivery.DeliveryAttempt,
            _options.MaxDeliveryAttempts,
            delay);

        await delivery.RetryAsync(delay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets a message aside, loudly. The reason travels three ways — to the transport, to the log and
    /// to the metric — because a parked message is the one outcome that nothing resolves on its own.
    /// </summary>
    private async Task ParkAsync(
        IMessageDelivery delivery, MessageParkReason reason, string detail, Exception failure, CancellationToken cancellationToken)
    {
        var message = delivery.Message;

        _telemetry.MessageParked(message.EventName, reason);

        _logger.LogError(
            failure,
            "Integration event {EventId} of type {EventName} is parked after delivery attempt {AttemptNumber} because of {ParkReason}: {ParkDetail}. It will not be handled without intervention.",
            message.EventId,
            message.EventName,
            delivery.DeliveryAttempt,
            reason,
            detail);

        await delivery.ParkAsync($"{reason}: {detail}", cancellationToken).ConfigureAwait(false);
    }
}
