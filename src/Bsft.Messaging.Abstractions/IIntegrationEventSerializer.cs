namespace Bsft.Messaging;

/// <summary>
/// Defines how an integration event is encoded into the payload of a <see cref="IntegrationEventMessage"/>
/// and decoded again. It is the one place the payload format is decided, so the side that stages an
/// event and the side that handles it can never disagree about it.
/// </summary>
public interface IIntegrationEventSerializer
{
    /// <summary>
    /// Encodes an integration event for the <see cref="IntegrationEventMessage.Payload"/>.
    /// </summary>
    /// <param name="integrationEvent">The event to encode.</param>
    /// <returns>The encoded event.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="integrationEvent"/> is null.</exception>
    string Serialize(IIntegrationEvent integrationEvent);

    /// <summary>
    /// Decodes the payload of a message into the event type registered under its
    /// <see cref="IntegrationEventMessage.EventName"/>.
    /// </summary>
    /// <param name="message">The message to decode.</param>
    /// <returns>The decoded event.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the event name is not registered in this process, or when the payload does not
    /// decode into an event.
    /// </exception>
    IIntegrationEvent Deserialize(IntegrationEventMessage message);
}
