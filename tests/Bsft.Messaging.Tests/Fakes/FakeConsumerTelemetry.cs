using Bsft.Messaging.Diagnostics;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>Records the counters the consumer emits, so the tests can assert on them by name.</summary>
public sealed class FakeConsumerTelemetry : IConsumerTelemetry
{
    private readonly List<string> _received = [];
    private readonly List<string> _handled = [];
    private readonly List<string> _duplicates = [];
    private readonly List<string> _retries = [];
    private readonly List<(string EventName, MessageParkReason Reason)> _parked = [];

    public IReadOnlyList<string> Received => _received;

    public IReadOnlyList<string> Handled => _handled;

    public IReadOnlyList<string> Duplicates => _duplicates;

    public IReadOnlyList<string> Retries => _retries;

    public IReadOnlyList<(string EventName, MessageParkReason Reason)> Parked => _parked;

    public int DeliverySpans { get; private set; }

    public void MessageReceived(string eventName) => _received.Add(eventName);

    public void MessageHandled(string eventName) => _handled.Add(eventName);

    public void DuplicateDropped(string eventName) => _duplicates.Add(eventName);

    public void DeliveryRetried(string eventName) => _retries.Add(eventName);

    public void MessageParked(string eventName, MessageParkReason reason) => _parked.Add((eventName, reason));

    public IDisposable? BeginDelivery(string eventName)
    {
        DeliverySpans++;
        return null;
    }
}
