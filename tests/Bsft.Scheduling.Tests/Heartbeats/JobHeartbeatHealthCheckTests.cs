using Bsft.Scheduling.Heartbeats;
using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Bsft.Scheduling.Tests.Heartbeats;

public sealed class JobHeartbeatHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

    private static readonly JobDefinition Hourly =
        new("hourly", typeof(RecordingJob), JobSchedule.Every(TimeSpan.FromHours(1)), JobOverlapPolicy.Forbid);

    private static readonly JobDefinition OnDemand =
        new("on-demand", typeof(RecordingJob), null, JobOverlapPolicy.Forbid);

    private readonly FixedTimeProvider _time = new(Now);
    private readonly JobOptionsMonitor _options = JobOptionsMonitor.Defaults;
    private readonly ServiceProvider _provider;
    private readonly InMemoryJobHeartbeatStore _heartbeats;

    public JobHeartbeatHealthCheckTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobLog());
        services.AddScoped<RecordingJob>();
        services.AddScoped<SelfSchedulingJob>();
        _provider = services.BuildServiceProvider();
        _heartbeats = new InMemoryJobHeartbeatStore(_time);
    }

    [Fact]
    public async Task AHostWithNoRecurringJobIsHealthyAndSaysNothingIsWatched()
    {
        var result = await CheckAsync(OnDemand);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("No recurring job is being watched for liveness.");
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task AJobSilentWithinItsToleranceIsHealthyAndMeasuredFromTheStart()
    {
        // Two hours of an hourly job is exactly the factor, so it is not yet overdue.
        _time.Advance(TimeSpan.FromHours(2));

        var result = await CheckAsync(Hourly);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("1 watched");

        var snapshot = (JobHeartbeatSnapshot)result.Data["hourly"];
        snapshot.LastCompletedAt.Should().BeNull();
        snapshot.MeasuredFrom.Should().Be(Now);
        snapshot.Silence.Should().Be(TimeSpan.FromHours(2));
        snapshot.Tolerance.Should().Be(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));
        snapshot.ExpectedInterval.Should().Be(TimeSpan.FromHours(1));
        snapshot.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task AJobSilentBeyondItsToleranceIsDegradedAndNamed()
    {
        _time.Advance(TimeSpan.FromHours(4));

        var result = await CheckAsync(Hourly);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("hourly");
        ((JobHeartbeatSnapshot)result.Data["hourly"]).IsOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task ACompletedRunMovesTheBaselineAndClearsTheOverdueState()
    {
        _time.Advance(TimeSpan.FromHours(4));
        await _heartbeats.RecordCompletedAsync("hourly");
        _time.Advance(TimeSpan.FromMinutes(10));

        var result = await CheckAsync(Hourly);

        result.Status.Should().Be(HealthStatus.Healthy);

        var snapshot = (JobHeartbeatSnapshot)result.Data["hourly"];
        snapshot.LastCompletedAt.Should().Be(Now.AddHours(4));
        snapshot.Silence.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task AnExplicitToleranceReplacesTheDerivedOneEntirely()
    {
        _options.For("hourly", options => options.HeartbeatTolerance = TimeSpan.FromDays(1));
        _time.Advance(TimeSpan.FromHours(6));

        var result = await CheckAsync(Hourly);

        result.Status.Should().Be(HealthStatus.Healthy);

        var snapshot = (JobHeartbeatSnapshot)result.Data["hourly"];
        snapshot.Tolerance.Should().Be(TimeSpan.FromDays(1));
        snapshot.ExpectedInterval.Should().BeNull();
    }

    [Fact]
    public async Task ADisabledJobIsNotLateItIsOff()
    {
        _options.For("hourly", options => options.Enabled = false);
        _time.Advance(TimeSpan.FromDays(7));

        var result = await CheckAsync(Hourly);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task AJobWhoseWatchIsTurnedOffIsNotWatched()
    {
        _options.For("hourly", options => options.IsHeartbeatMonitored = false);
        _time.Advance(TimeSpan.FromDays(7));

        var result = await CheckAsync(Hourly);

        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task ACronScheduleIsMeasuredByItsNextTwoOccurrences()
    {
        var nightly = new JobDefinition(
            "nightly", typeof(RecordingJob), JobSchedule.Cron("0 3 * * *"), JobOverlapPolicy.Forbid);

        var result = await CheckAsync(nightly);

        ((JobHeartbeatSnapshot)result.Data["nightly"]).ExpectedInterval.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task AScheduleWithNoTwoFurtherOccurrencesIsLeftUnwatched()
    {
        // A valid cron expression that can never match again gives nothing to derive a cadence from.
        var never = new JobDefinition(
            "never", typeof(RecordingJob), JobSchedule.Cron("0 0 30 2 *"), JobOverlapPolicy.Forbid);

        var result = await CheckAsync(never);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task AJobWhoseScheduleLivesOnItsTypeIsWatchedToo()
    {
        var self = new JobDefinition("self", typeof(SelfSchedulingJob), null, JobOverlapPolicy.Forbid);

        var result = await CheckAsync(self);

        result.Data.Should().ContainKey("self");
    }

    [Fact]
    public async Task AToleranceThatWouldOverflowSaturatesInsteadOfTakingTheCheckDown()
    {
        // TimeSpan * double throws on overflow, which would turn the health endpoint into a 500 rather
        // than report a job that is configured never to be late.
        _options.For("hourly", options => options.HeartbeatToleranceFactor = 1e15);
        _time.Advance(TimeSpan.FromDays(365));

        var result = await CheckAsync(Hourly);

        result.Status.Should().Be(HealthStatus.Healthy);
        ((JobHeartbeatSnapshot)result.Data["hourly"]).Tolerance.Should().Be(TimeSpan.MaxValue);
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var registry = new JobRegistry([Hourly], _options);
        var resolver = new JobScheduleResolver(registry, _provider.GetRequiredService<IServiceScopeFactory>());

        ((Action)(() => new JobHeartbeatHealthCheck(null!, resolver, _heartbeats, _options, _time)))
            .Should().Throw<ArgumentNullException>().WithParameterName("registry");
        ((Action)(() => new JobHeartbeatHealthCheck(registry, null!, _heartbeats, _options, _time)))
            .Should().Throw<ArgumentNullException>().WithParameterName("schedules");
        ((Action)(() => new JobHeartbeatHealthCheck(registry, resolver, null!, _options, _time)))
            .Should().Throw<ArgumentNullException>().WithParameterName("heartbeats");
        ((Action)(() => new JobHeartbeatHealthCheck(registry, resolver, _heartbeats, null!, _time)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        ((Action)(() => new JobHeartbeatHealthCheck(registry, resolver, _heartbeats, _options, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    private Task<HealthCheckResult> CheckAsync(params JobDefinition[] definitions)
    {
        var registry = new JobRegistry(definitions, _options);

        var check = new JobHeartbeatHealthCheck(
            registry,
            new JobScheduleResolver(registry, _provider.GetRequiredService<IServiceScopeFactory>()),
            _heartbeats,
            _options,
            _time);

        return check.CheckHealthAsync(new HealthCheckContext());
    }
}
