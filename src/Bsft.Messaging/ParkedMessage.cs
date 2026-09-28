namespace Bsft.Messaging;

/// <summary>
/// A message <see cref="Bsft.Messaging.Loopback.LoopbackMessageTransport"/> set aside, with the reason the consumer gave.
/// </summary>
/// <param name="Message">The message that was parked.</param>
/// <param name="Reason">Why the consumer parked it.</param>
/// <param name="ParkedAt">The instant at which it was parked.</param>
public sealed record ParkedMessage(IntegrationEventMessage Message, string Reason, DateTimeOffset ParkedAt);
