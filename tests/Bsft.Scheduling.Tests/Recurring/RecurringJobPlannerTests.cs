using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Scheduling.Tests.Recurring;

public class RecurringJobPlannerTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private static readonly JobDefinition _hourly =
        new("hourly", typeof(RecordingJob), JobSchedule.Every(TimeSpan.FromHours(1)), JobOverlapPolicy.Forbid);

    private static readonly JobDefinition _oneShot =
        new("oneshot", typeof(RecordingJob), null, JobOverlapPolicy.Forbid);

    private static readonly JobDefinition _selfScheduling =
        new("self", typeof(SelfSchedulingJob), null, JobOverlapPolicy.Forbid);

    private readonly InMemoryJobStore _store = new();
    private readonly FixedTimeProvider _time = new(_now);
    private readonly JobOptionsMonitor _jobOptions = JobOptionsMonitor.Defaults;
    private readonly ServiceProvider _provider;

    public RecurringJobPlannerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobLog());
        services.AddScoped<RecordingJob>();
        services.AddScoped<SelfSchedulingJob>();
        _provider = services.BuildServiceProvider();
    }

    private RecurringJobPlanner CreatePlanner(params JobDefinition[] registrations)
    {
        var registry = new JobRegistry(registrations, _jobOptions);

        return new RecurringJobPlanner(
            _store,
            registry,
            new JobScheduleResolver(registry, _provider.GetRequiredService<IServiceScopeFactory>()),
            _time,
            NullLogger<RecurringJobPlanner>.Instance);
    }

    [Fact]
    public async Task TheNextOccurrenceIsWrittenAsAPendingRecurringRun()
    {
        (await CreatePlanner(_hourly).TryScheduleNextAsync(_hourly.Name, _now)).Should().BeTrue();

        var run = (await _store.ListClaimableAsync(_now.AddHours(2), 10))[0];
        run.JobName.Should().Be("hourly");
        run.DueAt.Should().Be(_now.AddHours(1));
        run.IsRecurring.Should().BeTrue();
        run.Status.Should().Be(JobRunStatus.Pending);
    }

    [Fact]
    public async Task AOneShotRegistrationNeverGetsAnOccurrence()
    {
        (await CreatePlanner(_oneShot).TryScheduleNextAsync(_oneShot.Name, _now)).Should().BeFalse();
    }

    [Fact]
    public async Task AJobDeclaringItsOwnScheduleIsResolvedFromAnInstance()
    {
        (await CreatePlanner(_selfScheduling).TryScheduleNextAsync(_selfScheduling.Name, _now)).Should().BeTrue();

        (await _store.ListClaimableAsync(_now.AddHours(1), 10))[0].DueAt.Should().Be(_now.AddMinutes(10));
    }

    [Fact]
    public async Task ADeclaredScheduleIsOnlyResolvedOnce()
    {
        // The schedule is a constant of the type, so a second planning call must reuse it rather than
        // resolving another instance out of the container.
        var planner = CreatePlanner(_selfScheduling);

        await planner.TryScheduleNextAsync(_selfScheduling.Name, _now);
        var claimed = await ClaimAndSettleAsync("self");
        await planner.TryScheduleNextAsync(_selfScheduling.Name, claimed.DueAt);

        (await _store.ListClaimableAsync(_now.AddHours(1), 10)).Should().HaveCount(1);
    }

    [Fact]
    public async Task ARunThatFellBehindReturnsToItsGridInsteadOfReplayingEveryMissedTick()
    {
        // The occurrence that just finished was due long ago; anchoring on it alone would produce an
        // occurrence that is already overdue, and the dispatcher would spin catching up.
        _time.Now = _now.AddDays(1);

        await CreatePlanner(_hourly).TryScheduleNextAsync(_hourly.Name, _now);

        var run = (await _store.ListClaimableAsync(_now.AddDays(2), 10))[0];
        run.DueAt.Should().Be(_now.AddDays(1).AddHours(1));
    }

    [Fact]
    public async Task ASlowRunPushesTheFollowingOccurrenceOutRatherThanStackingOne()
    {
        // The run outlasted its own interval, so the next occurrence is measured from now.
        _time.Now = _now.AddMinutes(90);

        await CreatePlanner(_hourly).TryScheduleNextAsync(_hourly.Name, _now);

        (await _store.ListClaimableAsync(_now.AddDays(1), 10))[0].DueAt.Should().Be(_now.AddMinutes(150));
    }

    [Fact]
    public async Task AnOccurrenceIsNotDuplicatedWhileOneIsStillLive()
    {
        var planner = CreatePlanner(_hourly);

        (await planner.TryScheduleNextAsync(_hourly.Name, _now)).Should().BeTrue();
        (await planner.TryScheduleNextAsync(_hourly.Name, _now)).Should().BeFalse();
    }

    [Fact]
    public async Task AScheduleWithNoFurtherOccurrenceIsReportedRatherThanFailingSilently()
    {
        var exhausted = new JobDefinition(
            "exhausted", typeof(RecordingJob), JobSchedule.Cron("0 0 30 2 *"), JobOverlapPolicy.Forbid);

        (await CreatePlanner(exhausted).TryScheduleNextAsync(exhausted.Name, _now)).Should().BeFalse();
        (await _store.ListClaimableAsync(_now.AddYears(10), 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task SeedingGivesEveryRecurringRegistrationItsFirstOccurrence()
    {
        await CreatePlanner(_hourly, _selfScheduling, _oneShot).SeedAsync();

        var claimable = await _store.ListClaimableAsync(_now.AddHours(2), 10);
        claimable.Select(r => r.JobName).Should().BeEquivalentTo(["self", "hourly"]);
    }

    [Fact]
    public async Task SeedingTwiceDoesNotCreateASecondOccurrence()
    {
        // Every instance seeds at startup, and all but the first must find the work already there.
        var planner = CreatePlanner(_hourly);

        await planner.SeedAsync();
        await planner.SeedAsync();

        (await _store.ListClaimableAsync(_now.AddHours(2), 10)).Should().HaveCount(1);
    }

    [Fact]
    public async Task TheNullGuardRejectsAMissingJobName()
    {
        await CreatePlanner(_hourly).Invoking(p => p.TryScheduleNextAsync(null!, _now))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("jobName");
    }

    [Fact]
    public async Task AnUnregisteredJobNameYieldsNoOccurrence()
    {
        (await CreatePlanner(_hourly).TryScheduleNextAsync("never-registered", _now)).Should().BeFalse();
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var registry = new JobRegistry([_hourly], JobOptionsMonitor.Defaults);
        var resolver = new JobScheduleResolver(registry, _provider.GetRequiredService<IServiceScopeFactory>());
        var logger = NullLogger<RecurringJobPlanner>.Instance;

        ((Action)(() => new RecurringJobPlanner(null!, registry, resolver, _time, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        ((Action)(() => new RecurringJobPlanner(_store, null!, resolver, _time, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("registry");
        ((Action)(() => new RecurringJobPlanner(_store, registry, null!, _time, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("schedules");
        ((Action)(() => new RecurringJobPlanner(_store, registry, resolver, null!, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new RecurringJobPlanner(_store, registry, resolver, _time, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    private async Task<JobRun> ClaimAndSettleAsync(string jobName)
    {
        var candidate = (await _store.ListClaimableAsync(_now.AddYears(1), 10)).First(r => r.JobName == jobName);
        var claimed = await _store.ClaimAsync(candidate, "instance-a", _now.AddYears(1), _now.AddYears(1).AddMinutes(5));
        await _store.SettleAsync(claimed!, JobRunStatus.Completed, _now.AddYears(1));
        return claimed!;
    }

    [Fact]
    public async Task ADisabledJobIsGivenNoOccurrence()
    {
        _jobOptions.For("hourly", options => options.Enabled = false);

        (await CreatePlanner(_hourly).TryScheduleNextAsync("hourly", _now)).Should().BeFalse();
        (await _store.ListClaimableAsync(_now.AddYears(1), 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task SeedingSkipsTheDisabledJobsAndSeedsTheRest()
    {
        _jobOptions.For("hourly", options => options.Enabled = false);

        await CreatePlanner(_hourly, _selfScheduling).SeedAsync();

        var pending = await _store.ListClaimableAsync(_now.AddYears(1), 10);
        pending.Select(run => run.JobName).Should().Equal("self");
    }
}
