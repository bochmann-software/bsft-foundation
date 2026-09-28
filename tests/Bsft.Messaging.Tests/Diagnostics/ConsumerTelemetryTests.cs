using Bsft.Instrumentation;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
using System.Diagnostics;
using Xunit;
using Bsft.Instrumentation.DependencyInjection;
using Bsft.Messaging.Diagnostics;

namespace Bsft.Messaging.Tests.Diagnostics;

public class ConsumerTelemetryTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IMeterFactory _meterFactory;
    private readonly IInstrumentationSourceProvider _sources;
    private readonly MeterListener _listener = new();
    private readonly List<(string Instrument, long Value, string? EventName, string? ParkReason)> _measurements = [];

    public ConsumerTelemetryTests()
    {
        var services = new ServiceCollection();
        services.AddBsftInstrumentationSource(ConsumerTelemetry.SourceName);
        _provider = services.BuildServiceProvider();
        _meterFactory = _provider.GetRequiredService<IMeterFactory>();
        _sources = _provider.GetRequiredService<IInstrumentationSourceProvider>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            // Scoped to this test's own meter factory rather than to the meter name: a meter named
            // "Bsft.Messaging" built anywhere else in the process must not feed these assertions.
            if (ReferenceEquals(instrument.Meter.Scope, _meterFactory))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? eventName = null;
            string? parkReason = null;

            foreach (var tag in tags)
            {
                if (tag.Key == "messaging.event_name")
                    eventName = tag.Value as string;
                else if (tag.Key == "messaging.park_reason")
                    parkReason = tag.Value as string;
            }

            _measurements.Add((instrument.Name, value, eventName, parkReason));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    // Built per test rather than in the constructor, because this is what brings the underlying
    // ActivitySource into existence. A source that already exists is only reached by
    // ActivitySource.AddActivityListener walking the process-global active source list, and that
    // walk skips its tail element when another thread disposes a source while it runs, leaving the
    // source under test without a listener. Creating the source while the listener is already
    // registered attaches it through the ActivitySource constructor and avoids that walk entirely.
    private ConsumerTelemetry CreateTelemetry() => new(_sources);

    [Fact]
    public void Every_outcome_is_counted_under_its_own_instrument_and_tagged_with_the_event_name()
    {
        var telemetry = CreateTelemetry();

        telemetry.MessageReceived("orders.placed.v1");
        telemetry.MessageHandled("orders.placed.v1");
        telemetry.DuplicateDropped("orders.placed.v1");
        telemetry.DeliveryRetried("orders.placed.v1");

        _measurements.Select(m => m.Instrument).Should().Equal(
            "bsft.messaging.consumer.received",
            "bsft.messaging.consumer.handled",
            "bsft.messaging.consumer.duplicate",
            "bsft.messaging.consumer.retried");
        _measurements.Should().OnlyContain(m => m.Value == 1 && m.EventName == "orders.placed.v1");
    }

    [Fact]
    public void A_parked_message_is_tagged_with_its_reason_as_a_bounded_dimension()
    {
        CreateTelemetry().MessageParked("orders.placed.v1", MessageParkReason.UnknownEventName);

        var parked = _measurements.Should().ContainSingle().Subject;
        parked.Instrument.Should().Be("bsft.messaging.consumer.parked");
        parked.EventName.Should().Be("orders.placed.v1");
        parked.ParkReason.Should().Be(nameof(MessageParkReason.UnknownEventName));
    }

    [Fact]
    public void The_reason_a_transport_parks_under_is_a_dimension_of_the_same_metric()
    {
        // A transport parks the deliveries that never become a message at all. Its reason belongs to the
        // same closed set, so the metric stays enumerable rather than growing a series the moment a
        // transport invents a token of its own.
        CreateTelemetry().MessageParked("orders.placed.v1", MessageParkReason.UnreadableEnvelope);

        var parked = _measurements.Should().ContainSingle().Subject;
        parked.ParkReason.Should().Be(nameof(MessageParkReason.UnreadableEnvelope));
    }

    [Fact]
    public void A_delivery_span_carries_the_event_name_when_somebody_is_listening()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ConsumerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        // Only now, with the listener registered, does the activity source come into existence.
        using var span = CreateTelemetry().BeginDelivery("orders.placed.v1");

        var activity = span.Should().BeOfType<Activity>().Subject;
        activity.OperationName.Should().Be("messaging.consume");
        activity.GetTagItem("messaging.event_name").Should().Be("orders.placed.v1");
    }

    [Fact]
    public void A_delivery_span_is_absent_when_nobody_is_listening()
    {
        // Emission is a no-op without a listener, so instrumentation can be left in unconditionally.
        CreateTelemetry().BeginDelivery("orders.placed.v1").Should().BeNull();
    }

    [Fact]
    public void A_null_source_provider_is_rejected()
    {
        var act = () => new ConsumerTelemetry(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("sources");
    }
}
