using RabbitMQ.Client;
using System.Globalization;
using System.Text;

namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// Maps the wire envelope onto AMQP message properties and back.
/// </summary>
/// <remarks>
/// <para>
/// The envelope's fields go into the AMQP properties that mean the same thing rather than into the
/// body, so a broker's management UI, a routing rule or a consumer written against another framework
/// can read them without knowing anything about this one.
/// </para>
/// <para>
/// Two of them do not fit AMQP's fixed properties and travel in headers. The message version has no
/// standard property at all. The instant does have one, but <c>timestamp</c> is a whole number of
/// seconds, so it cannot carry back the <c>OccurredAt</c> that was published: the property is still
/// set, because that is the field broker tooling displays, and the full-precision value is written
/// alongside it so that the envelope a consumer rebuilds is the envelope the producer sent.
/// </para>
/// </remarks>
internal sealed class RabbitMQMessageMapper : IRabbitMQMessageMapper
{
    /// <summary>The header carrying <see cref="IntegrationEventMessage.MessageVersion"/>.</summary>
    internal const string MessageVersionHeader = "bsft-message-version";

    /// <summary>
    /// The header carrying <see cref="IntegrationEventMessage.OccurredAt"/> at full precision, next to
    /// the second-resolution AMQP timestamp.
    /// </summary>
    internal const string OccurredAtHeader = "bsft-occurred-at";

    /// <summary>
    /// The header a quorum queue stamps a redelivery with, holding how many earlier deliveries of the
    /// message were not acknowledged.
    /// </summary>
    internal const string DeliveryCountHeader = "x-delivery-count";

    /// <summary>
    /// The header carrying the reason the consumer host parked a message, put on the copy published to
    /// the dead-letter exchange.
    /// </summary>
    internal const string ParkReasonHeader = "x-park-reason";

    /// <summary>
    /// How much of a park reason travels with the parked message.
    /// </summary>
    /// <remarks>
    /// It is <see cref="MessageReasonLimits.MaxReasonLength"/>, which is also the width the outbox
    /// records a failure reason in, so the two sides of the pipeline cut an exception message at the
    /// same point rather than at two constants that merely happen to agree. The reason is unbounded — it
    /// quotes an exception, and so possibly the event — and an AMQP frame is not, so a reason nobody
    /// bounded would fail the very publish that preserves the message.
    /// </remarks>
    internal const int MaxParkReasonLength = MessageReasonLimits.MaxReasonLength;

    /// <summary>The attempt number reported for a queue that does not count redeliveries.</summary>
    private const int FirstAttempt = 1;

    /// <summary>The earliest instant an AMQP timestamp can be read back into.</summary>
    private static readonly long _minUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();

    /// <summary>The latest instant an AMQP timestamp can be read back into.</summary>
    private static readonly long _maxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    private readonly RabbitMQConnectionSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageMapper"/> class.
    /// </summary>
    /// <param name="settings">The settings supplying the content type and the application name.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    public RabbitMQMessageMapper(RabbitMQConnectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public BasicProperties ToProperties(IntegrationEventMessage message)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        return new BasicProperties
        {
            // Persistent, because the outbox has already told the database that this event exists. A
            // transient message would be dropped by a broker restart, and nothing would ever notice:
            // the outbox row was marked processed the moment the broker confirmed it.
            Persistent = true,
            MessageId = message.EventId.ToString(),
            CorrelationId = message.CorrelationId,
            Type = message.EventName,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            ContentType = _settings.ContentType,
            ContentEncoding = Encoding.UTF8.WebName,
            AppId = _settings.ApplicationName,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [MessageVersionHeader] = message.MessageVersion,
                [OccurredAtHeader] = message.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            },
        };
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> ToBody(IntegrationEventMessage message)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        return Encoding.UTF8.GetBytes(message.Payload);
    }

    /// <inheritdoc />
    public IntegrationEventMessage ToMessage(RabbitMQDelivery delivery)
    {
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));

        var properties = delivery.Properties;

        RejectForeignContentType(properties.ContentType);

        var headers = new RabbitMQHeaderTable(properties.Headers);

        return new IntegrationEventMessage
        {
            EventId = ReadEventId(properties.MessageId),
            EventName = ReadEventName(properties.Type, delivery.RoutingKey),

            // A message without the version header was written by a producer that predates it or does
            // not use this framework, and version 1 is what such a message is.
            MessageVersion = headers.Int32(MessageVersionHeader, IntegrationEventMessage.CurrentVersion),
            OccurredAt = ReadOccurredAt(properties, headers),
            CorrelationId = string.IsNullOrEmpty(properties.CorrelationId) ? null : properties.CorrelationId,
            Payload = Encoding.UTF8.GetString(delivery.Body.Span),
        };
    }

    /// <inheritdoc />
    public int ReadDeliveryAttempt(RabbitMQDelivery delivery)
    {
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));

        var redeliveries = new RabbitMQHeaderTable(delivery.Properties.Headers).Count(DeliveryCountHeader);

        // The header counts the deliveries that came before this one, so the attempt is one more. A
        // count the broker never wrote leaves it at zero and this delivery is the first attempt, which
        // is also the honest answer for a classic queue: it has no count, so every delivery is a first
        // one as far as anything here can tell.
        return redeliveries >= int.MaxValue ? int.MaxValue : (int)redeliveries + FirstAttempt;
    }

    /// <inheritdoc />
    public BasicProperties ToParkProperties(RabbitMQDelivery delivery, string reason)
    {
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));
        if (reason is null)
            throw new ArgumentNullException(nameof(reason));

        var source = delivery.Properties;

        // The headers are copied before the reason is added, so a message that already carried one —
        // a parked message somebody replayed — has it replaced rather than duplicated, and the rest of
        // what the producer sent still arrives in the dead-letter queue.
        var headers = source.Headers is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(source.Headers, StringComparer.Ordinal);

        headers[ParkReasonHeader] = Truncate(reason);

        // The envelope fields, and only those. Copying every AMQP property a foreign producer might
        // have set would be a second mapping to keep correct; these are the ones this adapter states
        // the envelope lives in, so a parked message stays readable as the event it was.
        return new BasicProperties
        {
            Persistent = true,
            MessageId = source.MessageId,
            CorrelationId = source.CorrelationId,
            Type = source.Type,
            Timestamp = source.Timestamp,
            ContentType = source.ContentType ?? _settings.ContentType,
            ContentEncoding = source.ContentEncoding ?? Encoding.UTF8.WebName,
            AppId = _settings.ApplicationName,
            Headers = headers,
        };
    }

    /// <summary>
    /// Cuts the park reason to the length a header may carry, without splitting a surrogate pair — a cut
    /// through a pair leaves an unpaired surrogate, and the publish that preserves the message is not
    /// the place to find out that the broker's encoder rejects one.
    /// </summary>
    private static string Truncate(string reason)
    {
        if (reason.Length <= MaxParkReasonLength)
            return reason;

        var length = MaxParkReasonLength;

        if (char.IsHighSurrogate(reason[length - 1]))
            length--;

        return reason[..length];
    }

    /// <summary>
    /// Refuses a delivery that states an encoding this application's serializer does not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The content type is the one thing a producer says about how the body is written, and the body is
    /// handed on to a serializer that reads exactly one encoding. A message labelled as something else
    /// is not a message this consumer can decode, and refusing it here parks it with the reason plainly
    /// stated instead of letting it fail later as an unreadable payload.
    /// </para>
    /// <para>
    /// A delivery that states nothing is accepted. This adapter labels everything it publishes, so an
    /// unlabelled message came from a producer that labels nothing at all rather than one that labels
    /// something different — the same tolerance the event name and the message version are read with.
    /// </para>
    /// <para>
    /// Only the media type is compared. The parameters after it describe the same encoding —
    /// <c>charset</c> is the usual one, and this adapter states the charset in <c>content-encoding</c>
    /// where AMQP puts it — so comparing them would reject messages over a difference in wording.
    /// </para>
    /// </remarks>
    private void RejectForeignContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return;

        if (!MediaTypeOf(contentType).Equals(MediaTypeOf(_settings.ContentType), StringComparison.OrdinalIgnoreCase))
            throw new RabbitMQMessageFormatException(
                $"The delivery states content type '{contentType}', and this application reads '{_settings.ContentType}'. The body is not written in an encoding its serializer can decode.");
    }

    /// <summary>Returns the media type of a content type, without the parameters that follow it.</summary>
    private static ReadOnlySpan<char> MediaTypeOf(string contentType)
    {
        var parameters = contentType.IndexOf(';', StringComparison.Ordinal);

        return (parameters < 0 ? contentType.AsSpan() : contentType.AsSpan(0, parameters)).Trim();
    }

    /// <summary>
    /// Reads the event id, refusing a delivery without a usable one. It is not a detail that can be
    /// defaulted: it is the key a consumer deduplicates on, and inventing one here would silently turn
    /// at-least-once delivery into "every redelivery is a new event".
    /// </summary>
    private static Guid ReadEventId(string? messageId) =>
        Guid.TryParse(messageId, out var eventId)
            ? eventId
            : throw new RabbitMQMessageFormatException(
                $"The delivery carries no usable message-id ('{messageId}'), so it has no event id to deduplicate on.");

    /// <summary>
    /// Reads the event name from the <c>type</c> property, falling back to the routing key. The two
    /// agree for anything this adapter published; the fallback is for a foreign producer that routes on
    /// the name without also setting the property.
    /// </summary>
    private static string ReadEventName(string? type, string routingKey)
    {
        if (!string.IsNullOrWhiteSpace(type))
            return type;

        if (!string.IsNullOrWhiteSpace(routingKey))
            return routingKey;

        throw new RabbitMQMessageFormatException(
            "The delivery carries neither a type property nor a routing key, so there is no event name to resolve a handler with.");
    }

    /// <summary>
    /// Reads the instant of the state change, preferring the full-precision header over the AMQP
    /// timestamp and falling back to it for a message this adapter did not publish.
    /// </summary>
    private static DateTimeOffset ReadOccurredAt(IReadOnlyBasicProperties properties, RabbitMQHeaderTable headers)
    {
        var header = headers.Text(OccurredAtHeader);

        if (header is not null && DateTimeOffset.TryParse(
                header, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredAt))
        {
            return occurredAt;
        }

        var unixSeconds = properties.Timestamp.UnixTime;

        // The timestamp is a 64-bit number a remote producer chooses freely, and
        // FromUnixTimeSeconds throws outside the range a date can represent. That exception would not
        // be a RabbitMQMessageFormatException, so it would escape the malformed-message handling
        // entirely and leave the delivery neither acknowledged nor rejected — one such message would
        // hold a prefetch slot for as long as the connection lives. An unusable timestamp is therefore
        // refused like any other unreadable field, and the message is dead-lettered where it is seen.
        if (unixSeconds < _minUnixSeconds || unixSeconds > _maxUnixSeconds)
            throw new RabbitMQMessageFormatException(
                $"The delivery's timestamp ({unixSeconds}) is outside the range a date can represent, and it carries no readable '{OccurredAtHeader}' header to fall back on.");

        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    }

}
