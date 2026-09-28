using Bsft.Scheduling.DependencyInjection;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Scheduling.Tests;

/// <summary>
/// Drives the fully wired scheduler over virtual time. Everything here runs through the real
/// container, the real dispatcher and the real store — only the clock is under the test's control,
/// so there is not a single wall-clock wait.
/// </summary>
public class SchedulingEndToEndTests
{
    private static readonly DateTimeOffset _start = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly JobLog _log = new();
    private readonly FixedTimeProvider _time = new(_start);

    private ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_log);
        services.AddSingleton<TimeProvider>(_time);
        configure(services);
        return services.BuildServiceProvider();
    }

    private static async Task<int> PumpAsync(ServiceProvider provider)
    {
        var dispatcher = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<JobDispatcher>()
            .Single();

        return await dispatcher.ProcessOnceAsync();
    }

    private static async Task SeedAsync(ServiceProvider provider) =>
        await provider.GetRequiredService<IRecurringJobPlanner>().SeedAsync();

    [Fact]
    public async Task ARecurringJobRunsOnceForEachOccurrenceOfItsSchedule()
    {
        using var provider = Build(s => s
            .AddBsftScheduling()
            .AddBsftRecurringJob<RecordingJob>(JobSchedule.Every(TimeSpan.FromMinutes(10)), "ticker"));

        await SeedAsync(provider);

        // Nothing is due yet: the first occurrence is ten minutes out.
        (await PumpAsync(provider)).Should().Be(0);
        _log.Count.Should().Be(0);

        for (var tick = 1; tick <= 3; tick++)
        {
            _time.Advance(TimeSpan.FromMinutes(10));
            (await PumpAsync(provider)).Should().Be(1);
        }

        _log.Count.Should().Be(3);
        _log.Executions.Select(e => e.ScheduledFor).Should().Equal(
            _start.AddMinutes(10), _start.AddMinutes(20), _start.AddMinutes(30));
    }

    [Fact]
    public async Task ACronScheduleFiresAtItsAppointedTime()
    {
        using var provider = Build(s => s
            .AddBsftScheduling()
            .AddBsftRecurringJob<RecordingJob>(JobSchedule.Cron("0 3 * * *"), "nightly"));

        await SeedAsync(provider);

        _time.Now = new DateTimeOffset(2026, 8, 4, 2, 59, 0, TimeSpan.Zero);
        (await PumpAsync(provider)).Should().Be(0);

        _time.Now = new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero);
        (await PumpAsync(provider)).Should().Be(1);

        _log.Executions.Should().ContainSingle()
            .Which.ScheduledFor.Should().Be(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task AJobDeclaringItsOwnScheduleNeedsNoFurtherConfiguration()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftRecurringJob<SelfSchedulingJob>("self"));

        await SeedAsync(provider);
        _time.Advance(TimeSpan.FromMinutes(10));

        (await PumpAsync(provider)).Should().Be(1);
        _log.Count.Should().Be(1);
    }

    [Fact]
    public async Task TwoInstancesSharingAStoreRunEachOccurrenceExactlyOnce()
    {
        // The headline guarantee of the lease: a scaled-out deployment does not multiply the work.
        var store = new InMemoryJobStore();

        using var first = Build(s => s
            .AddSingleton<IJobStore>(store)
            .AddBsftScheduling(o => o.InstanceId = "instance-a")
            .AddBsftRecurringJob<RecordingJob>(JobSchedule.Every(TimeSpan.FromMinutes(10)), "ticker"));
        using var second = Build(s => s
            .AddSingleton<IJobStore>(store)
            .AddBsftScheduling(o => o.InstanceId = "instance-b")
            .AddBsftRecurringJob<RecordingJob>(JobSchedule.Every(TimeSpan.FromMinutes(10)), "ticker"));

        await SeedAsync(first);
        await SeedAsync(second);

        _time.Advance(TimeSpan.FromMinutes(10));

        var claimedByFirst = await PumpAsync(first);
        var claimedBySecond = await PumpAsync(second);

        (claimedByFirst + claimedBySecond).Should().Be(1);
        _log.Count.Should().Be(1);
    }

    [Fact]
    public async Task ADelayedJobRunsOnceWhenItsDelayHasElapsed()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftJob<RecordingJob>("report"));

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IJobScheduler>()
                .EnqueueAsync("report", TimeSpan.FromMinutes(30), "{\"orderId\":42}");
        }

        (await PumpAsync(provider)).Should().Be(0);

        _time.Advance(TimeSpan.FromMinutes(30));
        (await PumpAsync(provider)).Should().Be(1);

        _log.Executions.Should().ContainSingle().Which.Payload.Should().Be("{\"orderId\":42}");

        // A one-shot job does not come back.
        _time.Advance(TimeSpan.FromDays(1));
        (await PumpAsync(provider)).Should().Be(0);
    }

    [Fact]
    public async Task ADelayedJobCanBeCalledOffBeforeItRuns()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftJob<RecordingJob>("report"));

        using (var scope = provider.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
            var id = await scheduler.EnqueueAsync("report", TimeSpan.FromMinutes(30));
            (await scheduler.CancelAsync(id)).Should().BeTrue();
        }

        _time.Advance(TimeSpan.FromHours(1));

        (await PumpAsync(provider)).Should().Be(0);
        _log.Count.Should().Be(0);
    }

    [Fact]
    public async Task ARecurringJobKeepsRunningAfterAnOccurrenceFails()
    {
        using var provider = Build(s => s
            .AddBsftScheduling(o => { o.MaxAttempts = 1; o.RetryDelay = TimeSpan.Zero; })
            .AddBsftRecurringJob<FailingJob>(JobSchedule.Every(TimeSpan.FromMinutes(10)), "flaky"));

        await SeedAsync(provider);

        for (var tick = 1; tick <= 3; tick++)
        {
            _time.Advance(TimeSpan.FromMinutes(10));
            await PumpAsync(provider);
        }

        // A failing job must not silently take its own schedule down with it.
        _log.Count.Should().Be(3);
    }

    [Fact]
    public async Task AFailedRunIsRetriedOnceItsBackoffHasElapsed()
    {
        using var provider = Build(s => s
            .AddBsftScheduling(o => { o.MaxAttempts = 3; o.RetryDelay = TimeSpan.FromMinutes(1); })
            .AddBsftJob<FailingJob>("flaky"));

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IJobScheduler>().EnqueueAsync("flaky", TimeSpan.Zero);
        }

        await PumpAsync(provider);
        _log.Count.Should().Be(1);

        // Still inside the backoff window.
        (await PumpAsync(provider)).Should().Be(0);

        _time.Advance(TimeSpan.FromMinutes(1));
        (await PumpAsync(provider)).Should().Be(1);

        _log.Count.Should().Be(2);
        _log.Executions[1].AttemptNumber.Should().Be(2);
    }

    [Fact]
    public async Task TheWorkOfACrashedInstanceIsPickedUpOnceItsLeaseExpires()
    {
        var store = new InMemoryJobStore();

        using var provider = Build(s => s
            .AddSingleton<IJobStore>(store)
            .AddBsftScheduling(o => o.LeaseDuration = TimeSpan.FromMinutes(5))
            .AddBsftJob<RecordingJob>("report"));

        var run = new JobRun { JobName = "report", DueAt = _start, Status = JobRunStatus.Pending };
        var id = await store.EnqueueAsync(run);

        // Stands in for an instance that claimed the run and then died.
        var candidate = (await store.FindAsync(id))!;
        await store.ClaimAsync(candidate, "crashed", _start, _start.AddMinutes(5));

        (await PumpAsync(provider)).Should().Be(0);

        _time.Advance(TimeSpan.FromMinutes(6));
        (await PumpAsync(provider)).Should().Be(1);

        _log.Count.Should().Be(1);
    }

    [Fact]
    public async Task AnOutageProducesOneCatchUpRunRatherThanABurstOfMissedTicks()
    {
        using var provider = Build(s => s
            .AddBsftScheduling()
            .AddBsftRecurringJob<RecordingJob>(JobSchedule.Every(TimeSpan.FromMinutes(1)), "ticker"));

        await SeedAsync(provider);

        // A day of downtime on a one-minute schedule would be 1440 missed ticks.
        _time.Advance(TimeSpan.FromDays(1));

        (await PumpAsync(provider)).Should().Be(1);
        _log.Count.Should().Be(1);

        // And the job is back on its grid rather than replaying the backlog.
        (await PumpAsync(provider)).Should().Be(0);
        _time.Advance(TimeSpan.FromMinutes(1));
        (await PumpAsync(provider)).Should().Be(1);
        _log.Count.Should().Be(2);
    }

    [Fact]
    public async Task AStaleOccurrenceIsDiscardedWhenTheCatchUpWindowSaysSo()
    {
        using var provider = Build(s => s
            .AddBsftScheduling(o =>
            {
                o.CatchUpPolicy = JobCatchUpPolicy.SkipBeyondMaxAge;
                o.MaxCatchUpAge = TimeSpan.FromHours(1);
            })
            .AddBsftRecurringJob<RecordingJob>(JobSchedule.Every(TimeSpan.FromMinutes(10)), "ticker"));

        await SeedAsync(provider);
        _time.Advance(TimeSpan.FromDays(1));

        await PumpAsync(provider);
        _log.Count.Should().Be(0);

        // The schedule survives the skip: the following occurrence still runs.
        _time.Advance(TimeSpan.FromMinutes(10));
        await PumpAsync(provider);
        _log.Count.Should().Be(1);
    }

    [Fact]
    public async Task AJobEnqueuedFromInsideAnotherJobInheritsItsCorrelationId()
    {
        using var provider = Build(s => s
            .AddBsftScheduling()
            .AddBsftJob<ChainingJob>("chaining")
            .AddBsftJob<RecordingJob>("report"));

        var store = provider.GetRequiredService<IJobStore>();
        await store.EnqueueAsync(new JobRun
        {
            JobName = "chaining",
            DueAt = _start,
            Status = JobRunStatus.Pending,
            CorrelationId = "corr-root",
        });

        await PumpAsync(provider);
        await PumpAsync(provider);

        _log.Executions.Should().ContainSingle()
            .Which.CorrelationId.Should().Be("corr-root");
    }

    /// <summary>Enqueues a follow-up job, standing in for a workflow that continues in the background.</summary>
    private sealed class ChainingJob : IJob
    {
        private readonly IJobScheduler _scheduler;

        public ChainingJob(IJobScheduler scheduler) => _scheduler = scheduler;

        public async Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default) =>
            await _scheduler.EnqueueAsync("report", TimeSpan.Zero, cancellationToken: ct);
    }
}
