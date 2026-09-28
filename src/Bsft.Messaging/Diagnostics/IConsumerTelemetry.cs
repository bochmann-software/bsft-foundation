namespace Bsft.Messaging.Diagnostics;

/// <summary>
/// Emits traces and metrics for the consuming side: how many messages arrived, how many were handled,
/// how many were recognised as redeliveries, and how many were retried or parked.
/// </summary>
/// <remarks>
/// The four outcome counters partition every message the host finishes with, so they can be read
/// against each other: handled plus duplicate is progress, retried is noise that should stay flat, and
/// parked is the only one that never resolves itself and is the one worth alerting on.
/// </remarks>
public interface IConsumerTelemetry
{
    /// <summary>Records that a message was received from the transport.</summary>
    /// <param name="eventName">The stable name the event travels under.</param>
    void MessageReceived(string eventName);

    /// <summary>Records that a message was handled and acknowledged.</summary>
    /// <param name="eventName">The stable name the event travels under.</param>
    void MessageHandled(string eventName);

    /// <summary>
    /// Records that a message was acknowledged without handling because the inbox already held a
    /// completed delivery of it.
    /// </summary>
    /// <param name="eventName">The stable name the event travels under.</param>
    void DuplicateDropped(string eventName);

    /// <summary>Records that a failed message was handed back to the transport for another attempt.</summary>
    /// <param name="eventName">The stable name the event travels under.</param>
    void DeliveryRetried(string eventName);

    /// <summary>Records that a message was set aside without being handled.</summary>
    /// <param name="eventName">The stable name the event travels under.</param>
    /// <param name="reason">The category the message was parked under.</param>
    void MessageParked(string eventName, MessageParkReason reason);

    /// <summary>
    /// Begins a telemetry scope spanning one delivery. Dispose it when the delivery finishes.
    /// </summary>
    /// <param name="eventName">The stable name the event travels under.</param>
    /// <returns>
    /// The scope to dispose, or <see langword="null"/> when nobody is listening for traces.
    /// </returns>
    IDisposable? BeginDelivery(string eventName);
}
