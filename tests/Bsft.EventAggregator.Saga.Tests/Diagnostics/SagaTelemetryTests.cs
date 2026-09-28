using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Tests.Support;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Diagnostics;

public sealed class SagaTelemetryTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly SagaTelemetry _telemetry;

    public SagaTelemetryTests()
    {
        _provider = new ServiceCollection()
            .AddBsftInstrumentationSource(SagaTelemetry.SourceName)
            .BuildServiceProvider();
        _telemetry = new SagaTelemetry(_provider.GetRequiredService<IInstrumentationSourceProvider>());
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        var act = () => new SagaTelemetry(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("sources");
    }

    [Fact]
    public void SagaStarted_IncrementsStartedCounter()
    {
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        _telemetry.SagaStarted("OrderSaga");

        capture.Measurements.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Instrument = "bsft.saga.started", SagaType = "OrderSaga" });
    }

    [Fact]
    public void SagaCompleted_IncrementsCompletedCounter()
    {
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        _telemetry.SagaCompleted("OrderSaga");

        capture.Measurements.Should().ContainSingle()
            .Which.Instrument.Should().Be("bsft.saga.completed");
    }

    [Fact]
    public void BeginStep_WithListener_SetsTagsAndRecordsExecutedDuration()
    {
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        using (_telemetry.BeginStep("OrderSaga", "order-1", 0)) { }

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("saga.type").Should().Be("OrderSaga");
        activity.GetTagItem("saga.correlation_key").Should().Be("order-1");
        activity.GetTagItem("saga.step_index").Should().Be(0);

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.step.duration" && m.Outcome == "executed");
    }

    [Fact]
    public void BeginStep_Skipped_RecordsSkippedOutcome()
    {
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        using (var scope = _telemetry.BeginStep("OrderSaga", "order-1", 0))
        {
            scope.Skipped();
        }

        capture.Activities.Should().ContainSingle().Which.GetTagItem("saga.skipped").Should().Be(true);
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.step.duration" && m.Outcome == "skipped");
    }

    [Fact]
    public void BeginStep_NullCorrelationKey_DoesNotSetTag()
    {
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        using (_telemetry.BeginStep("OrderSaga", null, 0)) { }

        capture.Activities.Should().ContainSingle().Which.GetTagItem("saga.correlation_key").Should().BeNull();
    }

    [Fact]
    public void BeginStep_WithoutListener_StillRecordsDuration()
    {
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName + ".other");

        using (_telemetry.BeginStep("OrderSaga", "order-1", 0)) { }

        // No activity captured (different listener name), but the duration metric is unaffected.
        capture.Activities.Should().BeEmpty();
    }

    [Fact]
    public void StepScope_NullTelemetry_Throws()
    {
        var act = () => new SagaStepTelemetryScope(null!, null, "OrderSaga");
        act.Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
    }

    [Fact]
    public void StepScope_DisposedTwice_RecordsOnce()
    {
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        var scope = _telemetry.BeginStep("OrderSaga", "order-1", 0);
        scope.Dispose();
        scope.Dispose();

        capture.Measurements.Count(m => m.Instrument == "bsft.saga.step.duration").Should().Be(1);
    }

    public void Dispose() => _provider.Dispose();
}
