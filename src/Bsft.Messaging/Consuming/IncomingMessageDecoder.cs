namespace Bsft.Messaging.Consuming;

/// <summary>
/// The default <see cref="IIncomingMessageDecoder"/>: it checks the message contract version, resolves
/// the event name through the registry and decodes the payload through the serializer.
/// </summary>
/// <remarks>
/// The order of the three steps is the order in which they can be trusted. The version is read first,
/// because a message from a newer contract may mean something different by the very fields the next
/// two steps use. The name is resolved before the payload is touched, so nothing is ever deserialized
/// into a type the wire chose: only a name the application declared with
/// <see cref="BsftMessagingOptions.AddEvent{TEvent}"/> reaches the serializer, and an unknown one is
/// refused rather than guessed at.
/// </remarks>
internal sealed class IncomingMessageDecoder : IIncomingMessageDecoder
{
    private readonly IIntegrationEventTypeRegistry _registry;
    private readonly IIntegrationEventSerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncomingMessageDecoder"/> class.
    /// </summary>
    /// <param name="registry">The registry resolving an event name to its declared type.</param>
    /// <param name="serializer">The serializer decoding the payload.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public IncomingMessageDecoder(
        IIntegrationEventTypeRegistry registry, IIntegrationEventSerializer serializer)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <inheritdoc />
    public IIntegrationEvent Decode(IntegrationEventMessage message)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        if (message.MessageVersion > IntegrationEventMessage.CurrentVersion)
            throw new MessageDecodeException(
                MessageParkReason.UnsupportedMessageVersion,
                $"Message {message.EventId} was written under message contract version {message.MessageVersion}, but this consumer understands at most version {IntegrationEventMessage.CurrentVersion}. Upgrade the consumer before the producer publishes this version.");

        // An older contract version is read as it stands. Version 1 is the first, so nothing is older
        // yet; when a version 2 exists, a version 1 message still carries every field version 1
        // defined, which is what the promise of a versioned contract is worth.
        if (_registry.FindType(message.EventName) is null)
            throw new MessageDecodeException(
                MessageParkReason.UnknownEventName,
                $"The integration event name '{message.EventName}' is not registered, so message {message.EventId} cannot be handled here. Declare it with BsftMessagingOptions.AddEvent, or stop routing it to this consumer.");

        try
        {
            return _serializer.Deserialize(message);
        }
        catch (InvalidOperationException failure)
        {
            // The one type IIntegrationEventSerializer documents for a payload it cannot decode. It is
            // caught rather than allowed to escape because the two failures mean opposite things to
            // the host: this one is a property of the message and will repeat on every redelivery, so
            // the message is parked instead of spending its attempts. Nothing is swallowed — the
            // failure becomes the inner exception of the parked message.
            throw new MessageDecodeException(
                MessageParkReason.UndecodablePayload,
                $"The payload of message {message.EventId} did not decode into event '{message.EventName}'.",
                failure);
        }
    }
}
