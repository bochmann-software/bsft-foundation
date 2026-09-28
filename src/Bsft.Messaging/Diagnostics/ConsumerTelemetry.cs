using System.Diagnostics.Metrics;
using Bsft.Instrumentation;

namespace Bsft.Messaging.Diagnostics;

/// <summary>
/// Default <see cref="IConsumerTelemetry"/> implementation that emits through the vendor-neutral
/// "Bsft.Messaging" instrumentation source.
/// </summary>
internal sealed class ConsumerTelemetry : IConsumerTelemetry
{
    /// <summary>The activity source and meter name for messaging telemetry.</summary>
    internal const string SourceName = "Bsft.Messaging";

    private const string ReceivedMetric = "bsft.messaging.consumer.received";
    private const string HandledMetric = "bsft.messaging.consumer.handled";
    private const string DuplicateMetric = "bsft.messaging.consumer.duplicate";
    private const string RetriedMetric = "bsft.messaging.consumer.retried";
    private const string ParkedMetric = "bsft.messaging.consumer.parked";
    private const string EventNameTag = "messaging.event_name";
    private const string ParkReasonTag = "messaging.park_reason";
    private const string DeliveryActivity = "messaging.consume";

    private readonly IInstrumentationSource _source;
    private readonly Counter<long> _received;
    private readonly Counter<long> _handled;
    private readonly Counter<long> _duplicate;
    private readonly Counter<long> _retried;
    private readonly Counter<long> _parked;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsumerTelemetry"/> class.
    /// </summary>
    /// <param name="sources">The instrumentation source provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources"/> is null.</exception>
    public ConsumerTelemetry(IInstrumentationSourceProvider sources)
    {
        if (sources is null)
            throw new ArgumentNullException(nameof(sources));

        _source = sources.Get(SourceName);
        _received = _source.GetOrCreateCounter(ReceivedMetric);
        _handled = _source.GetOrCreateCounter(HandledMetric);
        _duplicate = _source.GetOrCreateCounter(DuplicateMetric);
        _retried = _source.GetOrCreateCounter(RetriedMetric);
        _parked = _source.GetOrCreateCounter(ParkedMetric);
    }

    /// <inheritdoc />
    public void MessageReceived(string eventName) =>
        _received.Add(1, new KeyValuePair<string, object?>(EventNameTag, eventName));

    /// <inheritdoc />
    public void MessageHandled(string eventName) =>
        _handled.Add(1, new KeyValuePair<string, object?>(EventNameTag, eventName));

    /// <inheritdoc />
    public void DuplicateDropped(string eventName) =>
        _duplicate.Add(1, new KeyValuePair<string, object?>(EventNameTag, eventName));

    /// <inheritdoc />
    public void DeliveryRetried(string eventName) =>
        _retried.Add(1, new KeyValuePair<string, object?>(EventNameTag, eventName));

    /// <inheritdoc />
    public void MessageParked(string eventName, MessageParkReason reason) =>
        _parked.Add(
            1,
            new KeyValuePair<string, object?>(EventNameTag, eventName),

            // The enum member name, not the free-text detail: this is a metric dimension, and the
            // detail is unbounded. Where the detail belongs is the log entry and the parked message.
            new KeyValuePair<string, object?>(ParkReasonTag, reason.ToString()));

    /// <inheritdoc />
    public IDisposable? BeginDelivery(string eventName)
    {
        var activity = _source.StartActivity(DeliveryActivity);
        activity?.SetTag(EventNameTag, eventName);

        return activity;
    }
}
