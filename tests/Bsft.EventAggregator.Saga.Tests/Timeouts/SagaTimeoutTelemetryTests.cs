using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Tests.Support;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Timeouts;

public class SagaTimeoutTelemetryTests
{
    [Theory]
    [InlineData("bsft.saga.timeout.scheduled")]
    [InlineData("bsft.saga.timeout.fired")]
    [InlineData("bsft.saga.timeout.canceled")]
    [InlineData("bsft.saga.timeout.missed")]
    [InlineData("bsft.saga.timeout.dead_lettered")]
    public void EachTimeoutCounter_IsEmittedWithTheSagaTypeTag(string instrument)
    {
        using var capture = new SagaTelemetryCapture("Bsft.Saga");
        var telemetry = BuildTelemetry();

        Emit(telemetry, instrument);

        capture.Measurements.Should()
            .ContainSingle(m => m.Instrument == instrument)
            .Which.SagaType.Should().Be("Sales");
    }

    private static void Emit(ISagaTelemetry telemetry, string instrument)
    {
        switch (instrument)
        {
            case "bsft.saga.timeout.scheduled":
                telemetry.TimeoutScheduled("Sales");
                break;
            case "bsft.saga.timeout.fired":
                telemetry.TimeoutFired("Sales");
                break;
            case "bsft.saga.timeout.canceled":
                telemetry.TimeoutCanceled("Sales");
                break;
            case "bsft.saga.timeout.dead_lettered":
                telemetry.TimeoutDeadLettered("Sales");
                break;
            default:
                telemetry.TimeoutMissed("Sales");
                break;
        }
    }

    private static ISagaTelemetry BuildTelemetry()
    {
        var services = new ServiceCollection();
        services.AddBsftInstrumentationSource("Bsft.Saga");
        services.AddBsftSaga();
        return services.BuildServiceProvider().GetRequiredService<ISagaTelemetry>();
    }
}
