using System.Diagnostics;
using System.Diagnostics.Metrics;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Scheduling.Tests.Diagnostics;

public class JobTelemetryTests
{
    private readonly IInstrumentationSourceProvider _sources;

    public JobTelemetryTests()
    {
        var services = new ServiceCollection();
        services.AddBsftInstrumentationSource(JobTelemetry.SourceName);
        _sources = services.BuildServiceProvider().GetRequiredService<IInstrumentationSourceProvider>();
    }

    private JobTelemetry CreateTelemetry() => new(_sources);

    [Fact]
    public void EmittingIsANoOpWhileNobodyIsListening()
    {
        // Instrumentation is left in place unconditionally, so it must be safe without an exporter.
        var telemetry = CreateTelemetry();

        telemetry.Invoking(t =>
        {
            t.JobEnqueued("report");
            t.JobClaimed("report");
            t.JobSkipped("report");
            using var scope = t.BeginExecution("report", 1, 1);
            scope.Succeeded();
        }).Should().NotThrow();
    }

    [Fact]
    public void TheCountersCarryTheJobName()
    {
        var measurements = new List<(string Instrument, long Value, string? JobName)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobTelemetry.SourceName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? jobName = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "job.name")
                    jobName = tag.Value as string;
            }

            measurements.Add((instrument.Name, value, jobName));
        });
        listener.Start();

        var telemetry = CreateTelemetry();
        telemetry.JobEnqueued("report");
        telemetry.JobClaimed("report");
        telemetry.JobSkipped("nightly");

        listener.RecordObservableInstruments();

        measurements.Should().Contain(("bsft.job.enqueued", 1L, "report"));
        measurements.Should().Contain(("bsft.job.claimed", 1L, "report"));
        measurements.Should().Contain(("bsft.job.skipped", 1L, "nightly"));
    }

    [Fact]
    public void AnExecutionSpanCarriesTheRunIdentityAndAttempt()
    {
        using var listener = CreateActivityListener(out var activities);

        var telemetry = CreateTelemetry();
        using (var scope = telemetry.BeginExecution("report", 42, 3))
        {
            scope.Succeeded();
        }

        var activity = activities.Should().ContainSingle().Subject;
        activity.OperationName.Should().Be("job.execute");
        activity.GetTagItem("job.name").Should().Be("report");
        activity.GetTagItem("job.id").Should().Be(42L);
        activity.GetTagItem("job.attempt").Should().Be(3);
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void AScopeThatWasNeverMarkedSucceededIsRecordedAsAFailure()
    {
        // The default matters: a scope unwound by an exception must not look successful.
        using var listener = CreateActivityListener(out var activities);

        var telemetry = CreateTelemetry();
        using (telemetry.BeginExecution("report", 42, 1))
        {
            // No call to Succeeded, standing in for a job that threw.
        }

        activities.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void TheExecutionDurationIsRecordedWithItsOutcome()
    {
        var outcomes = new List<string?>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobTelemetry.SourceName && instrument.Name == "bsft.job.execution.duration")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                    outcomes.Add(tag.Value as string);
            }
        });
        listener.Start();

        var telemetry = CreateTelemetry();
        using (var succeeded = telemetry.BeginExecution("report", 1, 1))
            succeeded.Succeeded();
        using (telemetry.BeginExecution("report", 2, 1))
        {
            // Left to fail.
        }

        outcomes.Should().Equal("succeeded", "failed");
    }

    [Fact]
    public void DisposingAScopeTwiceRecordsOnlyOneMeasurement()
    {
        using var listener = CreateActivityListener(out var activities);

        var telemetry = CreateTelemetry();
        var scope = telemetry.BeginExecution("report", 1, 1);
        scope.Dispose();
        scope.Dispose();

        activities.Should().ContainSingle();
    }

    [Fact]
    public void TheConstructorRejectsAMissingSourceProvider()
    {
        ((Action)(() => new JobTelemetry(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("sources");
    }

    [Fact]
    public void TheScopeConstructorRejectsMissingArguments()
    {
        var telemetry = CreateTelemetry();

        ((Action)(() => new JobExecutionTelemetryScope(null!, null, "report")))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
        ((Action)(() => new JobExecutionTelemetryScope(telemetry, null, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("jobName");
    }

    private static ActivityListener CreateActivityListener(out List<Activity> activities)
    {
        var captured = new List<Activity>();
        activities = captured;

        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == JobTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = captured.Add,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
