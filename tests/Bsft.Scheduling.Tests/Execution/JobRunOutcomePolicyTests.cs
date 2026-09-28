using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Heartbeats;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Scheduling.Tests.Execution;

public class JobRunOutcomePolicyTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryJobStore _store = new();
    private readonly FixedTimeProvider _time = new(_now);
    private readonly BsftSchedulingOptions _options = new() { MaxAttempts = 2, RetryDelay = TimeSpan.FromMinutes(1) };
    private readonly IJobTelemetry _telemetry = Substitute.For<IJobTelemetry>();

    private readonly InMemoryJobHeartbeatStore _heartbeats;

    public JobRunOutcomePolicyTests() => _heartbeats = new InMemoryJobHeartbeatStore(_time);

    private JobRunOutcomePolicy CreatePolicy() =>
        new(
            _store,
            Options.Create(_options),
            _telemetry,
            _heartbeats,
            _time,
            NullLogger<JobRunOutcomePolicy>.Instance);

    private async Task<JobRun> ClaimAsync(DateTimeOffset? dueAt = null)
    {
        var id = await _store.EnqueueAsync(new JobRun
        {
            JobName = "report",
            DueAt = dueAt ?? _now,
            Status = JobRunStatus.Pending,
        });

        var candidate = (await _store.FindAsync(id))!;
        return (await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5)))!;
    }

    [Fact]
    public void UnderRunAllNothingIsEverBeyondTheWindow()
    {
        var run = new JobRun { JobName = "report", DueAt = _now.AddYears(-10) };

        CreatePolicy().IsBeyondCatchUpWindow(run).Should().BeFalse();
    }

    [Fact]
    public void UnderSkipBeyondMaxAgeAStaleRunIsBeyondTheWindow()
    {
        _options.CatchUpPolicy = JobCatchUpPolicy.SkipBeyondMaxAge;
        _options.MaxCatchUpAge = TimeSpan.FromHours(1);

        var policy = CreatePolicy();

        policy.IsBeyondCatchUpWindow(new JobRun { DueAt = _now.AddHours(-5) }).Should().BeTrue();
        policy.IsBeyondCatchUpWindow(new JobRun { DueAt = _now.AddMinutes(-5) }).Should().BeFalse();
    }

    [Fact]
    public async Task CompletionIsRecordedWithTheCurrentInstant()
    {
        var claimed = await ClaimAsync();
        _time.Advance(TimeSpan.FromMinutes(2));

        await CreatePolicy().RecordCompletedAsync(claimed);

        var stored = await _store.FindAsync(claimed.Id);
        stored!.Status.Should().Be(JobRunStatus.Completed);
        stored.SettledAt.Should().Be(_now.AddMinutes(2));
    }

    [Fact]
    public async Task ASkipIsRecordedAndCounted()
    {
        var claimed = await ClaimAsync();

        await CreatePolicy().RecordSkippedAsync(claimed);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Skipped);
        _telemetry.Received(1).JobSkipped("report");
    }

    [Fact]
    public async Task AFailureWithAttemptsLeftBecomesARetryAtTheBackoffInstant()
    {
        var claimed = await ClaimAsync();

        await CreatePolicy().RecordFailureAsync(claimed, new InvalidOperationException("boom"));

        var stored = await _store.FindAsync(claimed.Id);
        stored!.Status.Should().Be(JobRunStatus.Pending);
        stored.DueAt.Should().Be(_now.AddMinutes(1));
    }

    [Fact]
    public async Task AFailureOnTheLastAttemptRetiresTheRun()
    {
        var claimed = await ClaimAsync();
        claimed.AttemptCount = _options.MaxAttempts;

        await CreatePolicy().RecordFailureAsync(claimed, new InvalidOperationException("boom"));

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Failed);
    }

    [Fact]
    public async Task AnUnregisteredRunIsRetired()
    {
        var claimed = await ClaimAsync();

        await CreatePolicy().RecordUnregisteredAsync(claimed);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Failed);
    }

    [Fact]
    public async Task TheNullGuardsRejectMissingArguments()
    {
        var policy = CreatePolicy();

        policy.Invoking(p => p.IsBeyondCatchUpWindow(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("run");
        await policy.Invoking(p => p.RecordCompletedAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
        await policy.Invoking(p => p.RecordSkippedAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
        await policy.Invoking(p => p.RecordFailureAsync(null!, new InvalidOperationException()))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
        await policy.Invoking(p => p.RecordUnregisteredAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var logger = NullLogger<JobRunOutcomePolicy>.Instance;

        ((Action)(() => new JobRunOutcomePolicy(null!, Options.Create(_options), _telemetry, _heartbeats, _time, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        ((Action)(() => new JobRunOutcomePolicy(_store, null!, _telemetry, _heartbeats, _time, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        ((Action)(() => new JobRunOutcomePolicy(_store, Options.Create(_options), null!, _heartbeats, _time, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
        ((Action)(() => new JobRunOutcomePolicy(_store, Options.Create(_options), _telemetry, null!, _time, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("heartbeats");
        ((Action)(() => new JobRunOutcomePolicy(_store, Options.Create(_options), _telemetry, _heartbeats, null!, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new JobRunOutcomePolicy(_store, Options.Create(_options), _telemetry, _heartbeats, _time, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ARunOfADisabledJobIsSettledAsSkippedAndCounted()
    {
        var claimed = await ClaimAsync();

        await CreatePolicy().RecordDisabledAsync(claimed);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Skipped);
        _telemetry.Received(1).JobSkipped("report");
    }

    [Fact]
    public async Task RecordingADisabledRunRejectsAMissingRun()
    {
        await CreatePolicy().Invoking(p => p.RecordDisabledAsync(null!)).Should()
            .ThrowAsync<ArgumentNullException>().WithParameterName("run");
    }

    [Fact]
    public async Task ACompletedRunLeavesAHeartbeatWhereItsCompletionIsRecorded()
    {
        var claimed = await ClaimAsync();

        await CreatePolicy().RecordCompletedAsync(claimed);

        (await _heartbeats.LastCompletedAtAsync("report")).Should().Be(_time.Now);
    }
}
