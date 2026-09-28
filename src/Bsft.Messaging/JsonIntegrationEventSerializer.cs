using System.Text.Json;

namespace Bsft.Messaging;

/// <summary>
/// The default <see cref="IIntegrationEventSerializer"/>: the JSON encoding that message version 1
/// defines. It resolves the payload's type through the <see cref="IIntegrationEventTypeRegistry"/>, so
/// only a declared event type is ever instantiated from a stored row or a received message.
/// </summary>
internal sealed class JsonIntegrationEventSerializer : IIntegrationEventSerializer
{
    private readonly IIntegrationEventTypeRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonIntegrationEventSerializer"/> class.
    /// </summary>
    /// <param name="registry">The registry resolving an event name back to its type.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> is null.</exception>
    public JsonIntegrationEventSerializer(IIntegrationEventTypeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc />
    public string Serialize(IIntegrationEvent integrationEvent)
    {
        if (integrationEvent is null)
            throw new ArgumentNullException(nameof(integrationEvent));

        // The runtime type is passed explicitly: serializing against the static IIntegrationEvent type
        // would write the two interface properties and drop everything the event actually carries.
        return JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
    }

    /// <inheritdoc />
    public IIntegrationEvent Deserialize(IntegrationEventMessage message)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        var eventType = _registry.FindType(message.EventName)
            ?? throw new InvalidOperationException(
                $"The integration event name '{message.EventName}' is not registered, so event {message.EventId} cannot be decoded. Declare it with BsftMessagingOptions.AddEvent.");

        // A malformed payload is reported as the InvalidOperationException this contract documents,
        // rather than as the JsonException that happens to be how this implementation encodes JSON.
        // Callers act on the contract: the consumer host parks an undecodable message on it, and it
        // must not have to know which encoding produced the message to recognise one.
        object? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize(message.Payload, eventType);
        }
        catch (JsonException failure)
        {
            throw Undecodable(message, failure);
        }
        catch (NotSupportedException failure)
        {
            // Raised when the event type or one of its members has no usable converter. It is a
            // different cause from malformed JSON but the same outcome for a caller — this payload does
            // not become this event — and it has to be reported through the documented type, or it
            // escapes every handler written against the contract.
            throw Undecodable(message, failure);
        }

        return decoded as IIntegrationEvent
            ?? throw new InvalidOperationException(
                $"The payload of integration event {message.EventId} of type '{message.EventName}' did not decode into an event.");
    }

    /// <summary>
    /// Reports a payload this implementation cannot decode as the type the contract documents, keeping
    /// the encoding-specific failure as the inner exception.
    /// </summary>
    private static InvalidOperationException Undecodable(IntegrationEventMessage message, Exception failure) =>
        new($"The payload of integration event {message.EventId} of type '{message.EventName}' could not be decoded.",
            failure);
}
