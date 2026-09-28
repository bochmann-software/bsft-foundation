using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Heartbeats;
using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Scheduling.Tests.Execution;

public class JobRunnerTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly JobLog _log = new();
    private readonly InMemoryJobStore _store = new();
    private readonly FixedTimeProvider _time = new(_now);
    private readonly BsftSchedulingOptions _options = new() { MaxAttempts = 2, RetryDelay = TimeSpan.FromMinutes(1) };
    private readonly IJobTelemetry _telemetry = Substitute.For<IJobTelemetry>();
    private readonly JobOptionsMonitor _jobOptions = JobOptionsMonitor.Defaults;
    private readonly ServiceProvider _provider;
    private readonly InMemoryJobHeartbeatStore _heartbeats;

    public JobRunnerTests()
    {
        _heartbeats = new InMemoryJobHeartbeatStore(_time);

        var services = new ServiceCollection();
        services.AddSingleton(_log);
        services.AddSingleton(_store);
        services.AddScoped<RecordingJob>();
        services.AddScoped<FailingJob>();
        services.AddScoped<SlotInspectingJob>();
        _provider = services.BuildServiceProvider();

        _telemetry.BeginExecution(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>())
            .Returns(Substitute.For<IJobExecutionTelemetryScope>());
    }

    private JobRunner CreateRunner(params JobDefinition[] definitions)
    {
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

        return CreateRunner(new JobExecutor(scopeFactory, _time, Options.Create(_options), _telemetry), definitions);
    }

    private JobRunner CreateRunner(IJobExecutor executor, params JobDefinition[] definitions)
    {
        var registry = new JobRegistry(definitions, _jobOptions);
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

        return new JobRunner(
            registry,
            executor,
            new RecurringJobPlanner(
                _store,
                registry,
                new JobScheduleResolver(registry, scopeFactory),
                _time,
                NullLogger<RecurringJobPlanner>.Instance),
            new JobRunOutcomePolicy(
                _store,
                Options.Create(_options),
                _telemetry,
                _heartbeats,
                _time,
                NullLogger<JobRunOutcomePolicy>.Instance));
    }

    private static JobDefinition OneShot(Type jobType = null!) =>
        new("report", jobType ?? typeof(RecordingJob), null, JobOverlapPolicy.Forbid);

    private static JobDefinition Recurring(JobOverlapPolicy policy, Type jobType = null!) =>
        new("hourly", jobType ?? typeof(RecordingJob), JobSchedule.Every(TimeSpan.FromHours(1)), policy);

    private async Task<JobRun> ClaimAsync(string jobName, bool isRecurring = false, DateTimeOffset? dueAt = null)
    {
        var run = new JobRun
        {
            JobName = jobName,
            DueAt = dueAt ?? _now,
            Status = JobRunStatus.Pending,
            IsRecurring = isRecurring,
        };

        var id = await _store.EnqueueAsync(run);
        var candidate = (await _store.FindAsync(id))!;
        return (await _store.ClaimAsync(candidate, "instance-a", _time.Now, _time.Now.AddMinutes(5)))!;
    }

    [Fact]
    public async Task ASuccessfulRunIsMarkedCompleted()
    {
        var claimed = await ClaimAsync("report");

        await CreateRunner(OneShot()).RunAsync(claimed);

        _log.Count.Should().Be(1);
        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Completed);
    }

    [Fact]
    public async Task AFailingRunIsRetriedWhileAttemptsRemain()
    {
        var claimed = await ClaimAsync("report");

        await CreateRunner(OneShot(typeof(FailingJob))).RunAsync(claimed);

        var stored = await _store.FindAsync(claimed.Id);
        stored!.Status.Should().Be(JobRunStatus.Pending);
        stored.DueAt.Should().Be(_now.AddMinutes(1));
        stored.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task AFailingRunIsGivenUpOnAfterTheLastAttempt()
    {
        var claimed = await ClaimAsync("report");
        claimed.AttemptCount = _options.MaxAttempts;

        await CreateRunner(OneShot(typeof(FailingJob))).RunAsync(claimed);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Failed);
    }

    [Fact]
    public async Task ARunNamingAJobThatNoLongerExistsIsRetiredRatherThanPolledForever()
    {
        var claimed = await ClaimAsync("removed");

        await CreateRunner(OneShot()).RunAsync(claimed);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Failed);
        _log.Count.Should().Be(0);
    }

    [Fact]
    public async Task ARecurringRunCreatesTheFollowingOccurrenceWhenItCompletes()
    {
        var claimed = await ClaimAsync("hourly", isRecurring: true);

        await CreateRunner(Recurring(JobOverlapPolicy.Forbid)).RunAsync(claimed);

        var next = (await _store.ListClaimableAsync(_now.AddHours(2), 10)).Single();
        next.DueAt.Should().Be(_now.AddHours(1));
    }

    [Fact]
    public async Task UnderForbidTheFollowingOccurrenceOnlyExistsAfterTheRunHasSettled()
    {
        // Asserted from inside the job: while it is executing, no second occurrence may be pending.
        var claimed = await ClaimAsync("hourly", isRecurring: true);

        await CreateRunner(Recurring(JobOverlapPolicy.Forbid, typeof(SlotInspectingJob))).RunAsync(claimed);

        SlotInspectingJob.PendingWhileRunning.Should().Be(0);
    }

    [Fact]
    public async Task UnderAllowTheFollowingOccurrenceIsCreatedBeforeTheRunStarts()
    {
        var claimed = await ClaimAsync("hourly", isRecurring: true);

        await CreateRunner(Recurring(JobOverlapPolicy.Allow, typeof(SlotInspectingJob))).RunAsync(claimed);

        SlotInspectingJob.PendingWhileRunning.Should().Be(1);
    }

    [Fact]
    public async Task ARecurringRunThatFailedStillProducesTheFollowingOccurrence()
    {
        var claimed = await ClaimAsync("hourly", isRecurring: true);
        claimed.AttemptCount = _options.MaxAttempts;

        await CreateRunner(Recurring(JobOverlapPolicy.Forbid, typeof(FailingJob))).RunAsync(claimed);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Failed);
        (await _store.ListClaimableAsync(_now.AddHours(2), 10)).Should().ContainSingle();
    }

    [Fact]
    public async Task ARetryingRecurringRunDoesNotGetASecondLiveOccurrence()
    {
        // The retry is itself pending, so it holds the single-occurrence slot until it settles.
        var claimed = await ClaimAsync("hourly", isRecurring: true);

        await CreateRunner(Recurring(JobOverlapPolicy.Forbid, typeof(FailingJob))).RunAsync(claimed);

        var live = await _store.ListClaimableAsync(_now.AddHours(2), 10);
        live.Should().ContainSingle();
        live[0].Id.Should().Be(claimed.Id);
    }

    [Fact]
    public async Task AnOverdueRunIsDiscardedWhenTheCatchUpWindowHasPassed()
    {
        _options.CatchUpPolicy = JobCatchUpPolicy.SkipBeyondMaxAge;
        _options.MaxCatchUpAge = TimeSpan.FromHours(1);

        var claimed = await ClaimAsync("report", dueAt: _now.AddHours(-5));

        await CreateRunner(OneShot()).RunAsync(claimed);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Skipped);
        _log.Count.Should().Be(0);
        _telemetry.Received(1).JobSkipped("report");
    }

    [Fact]
    public async Task AnOverdueRunInsideTheWindowStillRuns()
    {
        _options.CatchUpPolicy = JobCatchUpPolicy.SkipBeyondMaxAge;
        _options.MaxCatchUpAge = TimeSpan.FromHours(6);

        var claimed = await ClaimAsync("report", dueAt: _now.AddHours(-5));

        await CreateRunner(OneShot()).RunAsync(claimed);

        _log.Count.Should().Be(1);
    }

    [Fact]
    public async Task UnderRunAllEvenAVeryOverdueRunIsExecuted()
    {
        var claimed = await ClaimAsync("report", dueAt: _now.AddYears(-1));

        await CreateRunner(OneShot()).RunAsync(claimed);

        _log.Count.Should().Be(1);
    }

    [Fact]
    public async Task ASkippedRecurringOccurrenceStillProducesTheNextOne()
    {
        // Otherwise a recurring job would stop for good the first time an outage outlasted the window.
        _options.CatchUpPolicy = JobCatchUpPolicy.SkipBeyondMaxAge;
        _options.MaxCatchUpAge = TimeSpan.FromHours(1);

        var claimed = await ClaimAsync("hourly", isRecurring: true, dueAt: _now.AddHours(-5));

        await CreateRunner(Recurring(JobOverlapPolicy.Forbid)).RunAsync(claimed);

        (await _store.ListClaimableAsync(_now.AddHours(2), 10)).Should().ContainSingle();
    }

    [Fact]
    public async Task AnExecutionCancelledWhileTheDispatcherStopsLeavesTheRunClaimedForTheNextPass()
    {
        // A shutdown is not a verdict on the job: the run stays claimed and is recovered by whichever
        // instance is still up once the lease expires.
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var claimed = await ClaimAsync("report");

        await CreateRunner(new CancelingExecutor(), OneShot()).RunAsync(claimed, stopping.Token);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Running);
    }

    [Fact]
    public async Task AnExecutionCancelledAtTheLeaseBoundaryIsChargedAsAFailedAttempt()
    {
        // Nobody asked the dispatcher to stop, so the cancellation came from the job's own execution —
        // the lease boundary, typically. Leaving it unsettled would strand the run: it stays Running
        // with a lapsed lease, the poll reclaims it, and it is executed again forever.
        var claimed = await ClaimAsync("report");

        await CreateRunner(new CancelingExecutor(), OneShot()).RunAsync(claimed);

        var stored = await _store.FindAsync(claimed.Id);
        stored!.Status.Should().Be(JobRunStatus.Pending);
        stored.DueAt.Should().Be(_now.AddMinutes(1), "the retry delay applies as it does to any failure");
    }

    [Fact]
    public async Task AnExecutionCancelledAtTheLeaseBoundaryIsGivenUpOnceTheAttemptsAreUsedUp()
    {
        // MaxAttempts is only consulted when a failure is recorded, so a cancellation that settles
        // nothing would never reach a terminal state at all.
        var claimed = await ClaimAsync("report");
        claimed.AttemptCount = _options.MaxAttempts;

        await CreateRunner(new CancelingExecutor(), OneShot()).RunAsync(claimed);

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Failed);
    }

    [Fact]
    public async Task AnExecutorThatThrowsSynchronouslyIsStillRecordedAsAFailure()
    {
        // An implementation may throw before it ever returns a task. That must not escape past the
        // outcome inspection and stop the dispatcher for the life of the process.
        var claimed = await ClaimAsync("report");

        await CreateRunner(new SynchronouslyThrowingExecutor(), OneShot())
            .Invoking(r => r.RunAsync(claimed)).Should().NotThrowAsync();

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Pending);
    }

    [Fact]
    public async Task TheNullGuardRejectsAMissingRun()
    {
        await CreateRunner(OneShot()).Invoking(r => r.RunAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("claimed");
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var registry = new JobRegistry([OneShot()], _jobOptions);
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var executor = new JobExecutor(scopeFactory, _time, Options.Create(_options), _telemetry);
        var planner = new RecurringJobPlanner(
            _store, registry, new JobScheduleResolver(registry, scopeFactory), _time, NullLogger<RecurringJobPlanner>.Instance);
        var outcomes = new JobRunOutcomePolicy(
            _store, Options.Create(_options), _telemetry, _heartbeats, _time, NullLogger<JobRunOutcomePolicy>.Instance);

        ((Action)(() => new JobRunner(null!, executor, planner, outcomes)))
            .Should().Throw<ArgumentNullException>().WithParameterName("registry");
        ((Action)(() => new JobRunner(registry, null!, planner, outcomes)))
            .Should().Throw<ArgumentNullException>().WithParameterName("executor");
        ((Action)(() => new JobRunner(registry, executor, null!, outcomes)))
            .Should().Throw<ArgumentNullException>().WithParameterName("planner");
        ((Action)(() => new JobRunner(registry, executor, planner, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("outcomes");
    }

    /// <summary>Counts the pending occurrences of its own job while it is executing.</summary>
    private sealed class SlotInspectingJob : IJob
    {
        private readonly InMemoryJobStore _store;

        public SlotInspectingJob(InMemoryJobStore store) => _store = store;

        public static int PendingWhileRunning { get; private set; }

        public async Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default)
        {
            var live = await _store.ListClaimableAsync(_now.AddYears(1), 10, ct);
            PendingWhileRunning = live.Count(r => r.JobName == context.JobName && r.Status == JobRunStatus.Pending);
        }
    }

    /// <summary>Stands in for an execution cut short by a cancellation, whatever token raised it.</summary>
    private sealed class CancelingExecutor : IJobExecutor
    {
        public Task ExecuteAsync(Type jobType, JobRun run, CancellationToken ct = default) =>
            Task.FromCanceled(new CancellationToken(canceled: true));
    }

    /// <summary>Throws before returning a task at all, which a hand-written executor may well do.</summary>
    private sealed class SynchronouslyThrowingExecutor : IJobExecutor
    {
        public Task ExecuteAsync(Type jobType, JobRun run, CancellationToken ct = default) =>
            throw new InvalidOperationException("resolved nothing");
    }

    [Fact]
    public async Task ASuccessfulRunLeavesAHeartbeat()
    {
        var claimed = await ClaimAsync("report");

        await CreateRunner(OneShot()).RunAsync(claimed);

        (await _heartbeats.LastCompletedAtAsync("report")).Should().Be(_time.Now);
    }

    [Fact]
    public async Task AFailedRunLeavesNoHeartbeat()
    {
        var claimed = await ClaimAsync("report");

        await CreateRunner(OneShot(typeof(FailingJob))).RunAsync(claimed);

        (await _heartbeats.LastCompletedAtAsync("report")).Should().BeNull();
    }

    [Fact]
    public async Task AnOccurrenceOfADisabledJobIsSkippedWithoutBeingExecuted()
    {
        _jobOptions.For("hourly", options => options.Enabled = false);
        var claimed = await ClaimAsync("hourly", isRecurring: true);

        await CreateRunner(Recurring(JobOverlapPolicy.Forbid)).RunAsync(claimed);

        _log.Count.Should().Be(0);
        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Skipped);
        (await _heartbeats.LastCompletedAtAsync("hourly")).Should().BeNull();
    }

    [Fact]
    public async Task ADisabledJobIsNotGivenAFollowingOccurrence()
    {
        _jobOptions.For("hourly", options => options.Enabled = false);
        var claimed = await ClaimAsync("hourly", isRecurring: true);

        await CreateRunner(Recurring(JobOverlapPolicy.Forbid)).RunAsync(claimed);

        var pending = await _store.ListClaimableAsync(_now.AddYears(1), 10);
        pending.Should().BeEmpty();
    }
}
