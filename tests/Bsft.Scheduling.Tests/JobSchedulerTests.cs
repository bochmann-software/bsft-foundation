using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class JobSchedulerTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryJobStore _store = new();
    private readonly FixedTimeProvider _time = new(_now);
    private readonly IJobTelemetry _telemetry = Substitute.For<IJobTelemetry>();
    private readonly StubCorrelationAccessor _correlation = new();
    private readonly JobRegistry _registry = new(
        [new JobDefinition("report", typeof(RecordingJob), null, JobOverlapPolicy.Forbid)],
        JobOptionsMonitor.Defaults);

    private JobScheduler CreateScheduler() =>
        new(_store, _registry, _correlation, _time, _telemetry);

    [Fact]
    public async Task ADelayIsMeasuredFromTheProvidedClockNotTheWallClock()
    {
        var id = await CreateScheduler().EnqueueAsync("report", TimeSpan.FromMinutes(30));

        (await _store.FindAsync(id))!.DueAt.Should().Be(_now.AddMinutes(30));
    }

    [Fact]
    public async Task AnAbsoluteDueDateIsTakenAsGiven()
    {
        var id = await CreateScheduler().EnqueueAtAsync("report", _now.AddDays(1));

        (await _store.FindAsync(id))!.DueAt.Should().Be(_now.AddDays(1));
    }

    [Fact]
    public async Task AnEnqueuedRunIsAOneShotNotARecurringOccurrence()
    {
        var id = await CreateScheduler().EnqueueAsync("report", TimeSpan.FromMinutes(1));

        var run = await _store.FindAsync(id);
        run!.IsRecurring.Should().BeFalse();
        run.Status.Should().Be(JobRunStatus.Pending);
    }

    [Fact]
    public async Task ThePayloadIsCarriedThroughUntouched()
    {
        var id = await CreateScheduler().EnqueueAsync("report", TimeSpan.Zero, "{\"orderId\":42}");

        (await _store.FindAsync(id))!.Payload.Should().Be("{\"orderId\":42}");
    }

    [Fact]
    public async Task TheCurrentCorrelationIdIsStampedOntoTheRun()
    {
        _correlation.CorrelationId = "corr-99";

        var id = await CreateScheduler().EnqueueAsync("report", TimeSpan.Zero);

        (await _store.FindAsync(id))!.CorrelationId.Should().Be("corr-99");
    }

    [Fact]
    public async Task EnqueuingIsCounted()
    {
        await CreateScheduler().EnqueueAsync("report", TimeSpan.Zero);

        _telemetry.Received(1).JobEnqueued("report");
    }

    [Fact]
    public async Task AGenericEnqueueResolvesTheNameFromTheRegisteredType()
    {
        var id = await CreateScheduler().EnqueueAsync<RecordingJob>(TimeSpan.FromMinutes(5));

        (await _store.FindAsync(id))!.JobName.Should().Be("report");
    }

    [Fact]
    public async Task AnUnknownNameIsRejectedAtEnqueueTimeRatherThanSilentlyStored()
    {
        // Otherwise a typo becomes a row nobody can execute, noticed only when the work never happens.
        var act = async () => await CreateScheduler().EnqueueAsync("typo", TimeSpan.Zero);

        await act.Should().ThrowAsync<JobNotRegisteredException>().WithMessage("*typo*");
    }

    [Fact]
    public async Task AnUnregisteredTypeIsRejected()
    {
        var act = async () => await CreateScheduler().EnqueueAsync<FailingJob>(TimeSpan.Zero);

        await act.Should().ThrowAsync<JobNotRegisteredException>().WithMessage("*FailingJob*");
    }

    [Fact]
    public async Task APendingRunCanBeCancelled()
    {
        var scheduler = CreateScheduler();
        var id = await scheduler.EnqueueAsync("report", TimeSpan.FromHours(1));

        (await scheduler.CancelAsync(id)).Should().BeTrue();
        (await _store.FindAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task CancellingAnUnknownRunReportsFailure()
    {
        (await CreateScheduler().CancelAsync(404)).Should().BeFalse();
    }

    [Fact]
    public async Task TheNullGuardsRejectMissingArguments()
    {
        var scheduler = CreateScheduler();

        await scheduler.Invoking(s => s.EnqueueAsync(null!, TimeSpan.Zero))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("jobName");
        await scheduler.Invoking(s => s.EnqueueAtAsync(null!, _now))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("jobName");
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        ((Action)(() => new JobScheduler(null!, _registry, _correlation, _time, _telemetry)))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        ((Action)(() => new JobScheduler(_store, null!, _correlation, _time, _telemetry)))
            .Should().Throw<ArgumentNullException>().WithParameterName("registry");
        ((Action)(() => new JobScheduler(_store, _registry, null!, _time, _telemetry)))
            .Should().Throw<ArgumentNullException>().WithParameterName("correlation");
        ((Action)(() => new JobScheduler(_store, _registry, _correlation, null!, _telemetry)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new JobScheduler(_store, _registry, _correlation, _time, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
    }

    private sealed class StubCorrelationAccessor : IJobCorrelationAccessor
    {
        public string? CorrelationId { get; set; }
    }
}
