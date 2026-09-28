using Bsft.Idempotency.Diagnostics;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
using Xunit;

namespace Bsft.Idempotency.Tests.Diagnostics;

public class IdempotencyTelemetryTests : IDisposable
{
    private readonly List<(string Instrument, long Value)> _measurements = [];
    private readonly MeterListener _listener;

    public IdempotencyTelemetryTests()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == IdempotencyTelemetry.SourceName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };

        _listener.SetMeasurementEventCallback<long>(Record);
        _listener.Start();
    }

    [Theory]
    [InlineData(IdempotencyAuditAction.Replayed, "bsft.idempotency.replayed")]
    [InlineData(IdempotencyAuditAction.KeyReused, "bsft.idempotency.conflicted")]
    [InlineData(IdempotencyAuditAction.InProgress, "bsft.idempotency.in_progress")]
    public void Record_ADecision_CountsItOnItsOwnInstrument(IdempotencyAuditAction action, string instrument)
    {
        var telemetry = Telemetry();

        telemetry.Record(action);

        _measurements.Should().ContainSingle().Which.Should().Be((instrument, 1L));
    }

    [Fact]
    public void TheSourceName_MatchesThePackage() =>
        IdempotencyTelemetry.SourceName.Should().Be("Bsft.Idempotency");

    [Fact]
    public void Constructor_NullSourceProvider_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyTelemetry(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("sources");

    public void Dispose()
    {
        _listener.Dispose();
        GC.SuppressFinalize(this);
    }

    private static IIdempotencyTelemetry Telemetry()
    {
        var services = new ServiceCollection();

        services.AddBsftInstrumentationSource(IdempotencyTelemetry.SourceName);
        services.AddSingleton<IIdempotencyTelemetry, IdempotencyTelemetry>();

        return services.BuildServiceProvider().GetRequiredService<IIdempotencyTelemetry>();
    }

    private void Record(
        Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        _measurements.Add((instrument.Name, value));
}
