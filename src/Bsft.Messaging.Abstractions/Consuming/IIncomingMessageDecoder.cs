namespace Bsft.Messaging.Consuming;

/// <summary>
/// Turns a message that arrived over a transport into the event it stands for, or refuses it with a
/// reason. It is the boundary at which untrusted wire data becomes a typed event.
/// </summary>
public interface IIncomingMessageDecoder
{
    /// <summary>
    /// Decodes a received message into its integration event.
    /// </summary>
    /// <param name="message">The message as it came off the wire.</param>
    /// <returns>The decoded event.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    /// <exception cref="MessageDecodeException">
    /// Thrown when the message cannot be handled in this process, carrying the reason it is parked
    /// under: an unregistered event name, a message contract newer than this one, or a payload that
    /// does not decode.
    /// </exception>
    IIntegrationEvent Decode(IntegrationEventMessage message);
}
