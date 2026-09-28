using System.Diagnostics;
using Bsft.Core;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using Bsft.Mediator.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public sealed class MediatorTelemetryTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly MediatorTelemetry _telemetry;

    public MediatorTelemetryTests()
    {
        _provider = new ServiceCollection()
            .AddBsftInstrumentationSource(MediatorTelemetry.SourceName)
            .BuildServiceProvider();
        _telemetry = new MediatorTelemetry(_provider.GetRequiredService<IInstrumentationSourceProvider>());
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        var act = () => new MediatorTelemetry(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("sources");
    }

    [Fact]
    public void BeginRequest_WithListener_SetsTagsAndRecordsSuccess()
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginRequest("CreateOrder", "MyApp.CreateOrder", "abc-123"))
        {
            scope.Complete();
        }

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.DisplayName.Should().Be("CreateOrder");
        activity.GetTagItem("bsft.request_type").Should().Be("MyApp.CreateOrder");
        activity.GetTagItem("bsft.request_name").Should().Be("CreateOrder");
        activity.GetTagItem("correlation_id").Should().Be("abc-123");
        activity.Status.Should().Be(ActivityStatusCode.Unset);

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.mediator.requests" && m.Outcome == "success");
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.mediator.request.duration");
        capture.Measurements.Should().NotContain(m => m.Instrument == "bsft.mediator.failures");
    }

    [Theory]
    [InlineData("has space")]
    // A trailing newline is the one malformed value a $-anchored pattern lets through, and a tag is
    // exactly where it must not arrive.
    [InlineData("trailing-newline" + "\n")]
    [InlineData("")]
    [InlineData(null)]
    public void BeginRequest_InvalidOrMissingCorrelationId_DoesNotSetTag(string? correlationId)
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        using (_telemetry.BeginRequest("CreateOrder", "MyApp.CreateOrder", correlationId)) { }

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("correlation_id").Should().BeNull();
    }

    [Fact]
    public void Fail_WithExpectedBsftException_RecordsExpectedErrorWithoutFailure()
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginRequest("CreateOrder", "MyApp.CreateOrder", null))
        {
            scope.Fail(new BusinessRuleException("invalid"));
        }

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Unset);
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.mediator.requests" && m.Outcome == "expected_error");
        capture.Measurements.Should().NotContain(m => m.Instrument == "bsft.mediator.failures");
    }

    [Fact]
    public void Fail_WithUnexpectedException_SetsErrorStatusAndCountsFailure()
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginRequest("CreateOrder", "MyApp.CreateOrder", null))
        {
            scope.Fail(new InvalidOperationException("boom"));
        }

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        var exceptionEvent = activity.Events.Should().ContainSingle(e => e.Name == "exception").Subject;
        exceptionEvent.Tags.Should().Contain(new KeyValuePair<string, object?>("exception.type", typeof(InvalidOperationException).FullName));
        exceptionEvent.Tags.Should().Contain(new KeyValuePair<string, object?>("exception.message", "boom"));
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.mediator.requests" && m.Outcome == "error");
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.mediator.failures");
    }

    [Fact]
    public void Fail_AggregateOfExpectedExceptions_RecordsExpectedError()
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginRequest("OrderPlaced", "MyApp.OrderPlaced", null))
        {
            scope.Fail(new AggregateException(new BusinessRuleException("a"), new BusinessRuleException("b")));
        }

        capture.Activities.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Unset);
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.mediator.requests" && m.Outcome == "expected_error");
        capture.Measurements.Should().NotContain(m => m.Instrument == "bsft.mediator.failures");
    }

    [Fact]
    public void Fail_AggregateWithUnexpectedException_RecordsError()
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginRequest("OrderPlaced", "MyApp.OrderPlaced", null))
        {
            scope.Fail(new AggregateException(new BusinessRuleException("a"), new InvalidOperationException("boom")));
        }

        capture.Activities.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Error);
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.mediator.failures");
    }

    [Fact]
    public void Fail_EmptyAggregate_RecordsError()
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginRequest("OrderPlaced", "MyApp.OrderPlaced", null))
        {
            scope.Fail(new AggregateException());
        }

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.mediator.requests" && m.Outcome == "error");
    }

    [Fact]
    public void Scope_NullTelemetry_Throws()
    {
        var act = () => new RequestTelemetryScope(null!, null, "x");
        act.Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
    }

    [Fact]
    public void Fail_NullException_Throws()
    {
        using var scope = _telemetry.BeginRequest("CreateOrder", "MyApp.CreateOrder", null);
        var act = () => scope.Fail(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("exception");
    }

    [Fact]
    public void Dispose_CalledTwice_RecordsOnce()
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        var scope = _telemetry.BeginRequest("CreateOrder", "MyApp.CreateOrder", null);
        scope.Complete();
        scope.Dispose();
        scope.Dispose();

        capture.Measurements.Count(m => m.Instrument == "bsft.mediator.requests").Should().Be(1);
    }

    [Fact]
    public void BeginRequest_WithoutListener_StillRecordsMetrics()
    {
        // No TelemetryCapture: activity is not sampled, so the scope holds a null activity.
        using var metricCapture = new MetricOnlyCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginRequest("CreateOrder", "MyApp.CreateOrder", null))
        {
            scope.Complete();
        }

        metricCapture.RequestCount.Should().Be(1);
    }

    [Fact]
    public void Fail_WithoutListener_CountsFailureWithoutActivity()
    {
        using var metricCapture = new MetricOnlyCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginRequest("CreateOrder", "MyApp.CreateOrder", null))
        {
            scope.Fail(new InvalidOperationException("boom"));
        }

        metricCapture.RequestCount.Should().Be(1);
    }

    [Fact]
    public void BeginPublish_WithListener_SetsNotificationTags()
    {
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        using (var scope = _telemetry.BeginPublish("OrderPlaced", 3))
        {
            scope.Complete();
        }

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("bsft.notification_name").Should().Be("OrderPlaced");
        activity.GetTagItem("bsft.handler_count").Should().Be(3);
    }

    public void Dispose() => _provider.Dispose();
}
