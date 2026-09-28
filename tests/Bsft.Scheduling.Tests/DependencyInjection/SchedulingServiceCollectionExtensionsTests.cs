using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Scheduling.DependencyInjection;
using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Heartbeats;
using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.StoredSchedules;

namespace Bsft.Scheduling.Tests.DependencyInjection;

public class SchedulingServiceCollectionExtensionsTests
{
    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobLog());
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheDefaultRegistrationWiresEveryPieceOfTheScheduler()
    {
        using var provider = Build(s => s.AddBsftScheduling());

        provider.GetRequiredService<IJobStore>().Should().BeOfType<InMemoryJobStore>();
        provider.GetRequiredService<IJobRegistry>().Should().BeOfType<JobRegistry>();
        provider.GetRequiredService<IJobExecutor>().Should().BeOfType<JobExecutor>();
        provider.GetRequiredService<IRecurringJobPlanner>().Should().BeOfType<RecurringJobPlanner>();
        provider.GetRequiredService<IJobRunner>().Should().BeOfType<JobRunner>();
        provider.GetRequiredService<IJobTelemetry>().Should().BeOfType<JobTelemetry>();
        provider.GetRequiredService<IStoredScheduleStore>().Should().BeOfType<InMemoryStoredScheduleStore>();
        provider.GetRequiredService<IStoredScheduleService>().Should().BeOfType<StoredScheduleService>();
        provider.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value.Should().NotBeNull();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void TheSchedulerIsScopedSoAnEnqueuePicksUpTheRequestCorrelationId()
    {
        using var provider = Build(s => s.AddBsftScheduling());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IJobScheduler>().Should().BeOfType<JobScheduler>();
    }

    [Fact]
    public void TheSeederAndTheDispatcherAreBothHosted()
    {
        using var provider = Build(s => s.AddBsftScheduling());

        var hosted = provider.GetServices<IHostedService>().ToList();

        hosted.Should().ContainSingle(h => h is RecurringJobSeeder);
        hosted.Should().ContainSingle(h => h is StoredScheduleSeeder);
        hosted.Should().ContainSingle(h => h is JobDispatcher);
    }

    [Fact]
    public void TheSeederIsRegisteredAheadOfTheDispatcherSoTheFirstPollHasWorkToFind()
    {
        using var provider = Build(s => s.AddBsftScheduling());

        var hosted = provider.GetServices<IHostedService>().ToList();

        hosted.FindIndex(h => h is RecurringJobSeeder)
            .Should().BeLessThan(hosted.FindIndex(h => h is JobDispatcher));
    }

    [Fact]
    public void TheOptionsActionIsApplied()
    {
        using var provider = Build(s => s.AddBsftScheduling(o => o.BatchSize = 7));

        provider.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value.BatchSize.Should().Be(7);
    }

    [Fact]
    public void TheOptionsAreBoundFromTheConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Scheduling:BatchSize"] = "11",
                ["Bsft:Scheduling:PollInterval"] = "00:00:02",
                ["Bsft:Scheduling:InstanceId"] = "worker-from-config",
            })
            .Build();

        using var provider = Build(s => s.AddBsftScheduling(configuration));

        var options = provider.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value;
        options.BatchSize.Should().Be(11);
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(2));
        options.InstanceId.Should().Be("worker-from-config");
    }

    [Fact]
    public void TheDelegateRunsAfterTheSectionSoCodeWinsOverConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bsft:Scheduling:BatchSize"] = "11" })
            .Build();

        using var provider = Build(s => s.AddBsftScheduling(configuration, o => o.BatchSize = 12));

        provider.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value.BatchSize.Should().Be(12);
    }

    [Fact]
    public void TheConfigurationOverloadRejectsAMissingConfiguration()
    {
        ((Action)(() => new ServiceCollection().AddBsftScheduling((IConfiguration)null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        ((Action)(() => SchedulingServiceCollectionExtensions.AddBsftScheduling(null!, new ConfigurationBuilder().Build())))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AnInvalidConfigurationIsRefusedWhenTheOptionsAreRealisedRatherThanSilentlyDoingNothing()
    {
        using var provider = Build(s => s.AddBsftScheduling(o => o.LeaseDuration = TimeSpan.Zero));

        provider.Invoking(p => p.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value)
            .Should().Throw<OptionsValidationException>().WithMessage("*LeaseDuration*");
    }

    [Fact]
    public void AHostRegisteredStoreIsNotDisplacedByTheInMemoryDefault()
    {
        // The in-memory store goes in with TryAdd, so a persistence package — or a host with a store
        // of its own — keeps the registration whichever order the two calls are made in.
        var custom = new InMemoryJobStore();

        using var provider = Build(s =>
        {
            s.AddSingleton<IJobStore>(custom);
            s.AddBsftScheduling();
        });

        provider.GetRequiredService<IJobStore>().Should().BeSameAs(custom);
    }

    [Fact]
    public void AHostSuppliedClockIsNotOverwritten()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero));

        using var provider = Build(s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            s.AddBsftScheduling();
        });

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock);
    }

    [Fact]
    public void TheDefaultCorrelationAccessorOnlyKnowsTheAmbientId()
    {
        using var provider = Build(s => s.AddBsftScheduling());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IJobCorrelationAccessor>()
            .Should().BeOfType<AmbientJobCorrelationAccessor>();
    }

    [Fact]
    public void AJobIsRegisteredUnderItsFullTypeNameByDefault()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftJob<RecordingJob>());

        var registration = provider.GetRequiredService<IJobRegistry>().Definitions.Should().ContainSingle().Subject;
        registration.Name.Should().Be(typeof(RecordingJob).FullName);
        registration.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public void AJobCanBeGivenAnExplicitName()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftJob<RecordingJob>("report"));

        provider.GetRequiredService<IJobRegistry>().Find("report").Should().NotBeNull();
    }

    [Fact]
    public void AJobIsResolvableAsAScopedServiceSoItCanTakeAPerOperationDependency()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftJob<RecordingJob>());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<RecordingJob>().Should().NotBeNull();
    }

    [Fact]
    public void ARecurringJobUsesTheScheduleItDeclares()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftRecurringJob<SelfSchedulingJob>("self"));

        var registration = provider.GetRequiredService<IJobRegistry>().Find("self");
        registration!.Schedule.Should().BeNull();
        registration.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public void ARecurringJobCanBeGivenItsScheduleAtRegistrationTime()
    {
        var schedule = JobSchedule.Cron("0 3 * * *");

        using var provider = Build(s => s.AddBsftScheduling()
            .AddBsftRecurringJob<RecordingJob>(schedule, "nightly", JobOverlapPolicy.Allow));

        var registration = provider.GetRequiredService<IJobRegistry>().Find("nightly");
        registration!.Schedule.Should().BeSameAs(schedule);
        registration.OverlapPolicy.Should().Be(JobOverlapPolicy.Allow);
    }

    [Fact]
    public void OverlapIsForbiddenUnlessTheHostSaysOtherwise()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftRecurringJob<SelfSchedulingJob>("self"));

        provider.GetRequiredService<IJobRegistry>().Find("self")!.OverlapPolicy
            .Should().Be(JobOverlapPolicy.Forbid);
    }

    [Fact]
    public void TwoJobsUnderOneNameFailWhenTheRegistryIsBuilt()
    {
        using var provider = Build(s => s.AddBsftScheduling()
            .AddBsftJob<RecordingJob>("same")
            .AddBsftJob<FailingJob>("same"));

        provider.Invoking(p => p.GetRequiredService<IJobRegistry>())
            .Should().Throw<InvalidOperationException>().WithMessage("*must be unique*");
    }

    [Fact]
    public void TheNullGuardsRejectAMissingServiceCollection()
    {
        ((Action)(() => SchedulingServiceCollectionExtensions.AddBsftScheduling(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        ((Action)(() => SchedulingServiceCollectionExtensions.AddBsftJob<RecordingJob>(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        ((Action)(() => SchedulingServiceCollectionExtensions.AddBsftRecurringJob<SelfSchedulingJob>(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        ((Action)(() => SchedulingServiceCollectionExtensions.AddBsftRecurringJob<RecordingJob>(null!, JobSchedule.Every(TimeSpan.FromHours(1)))))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void ARecurringRegistrationWithoutAScheduleIsRejected()
    {
        ((Action)(() => new ServiceCollection().AddBsftRecurringJob<RecordingJob>(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("schedule");
    }

    [Fact]
    public void TheDefaultRegistrationWiresTheScheduleResolverAndTheHeartbeatStore()
    {
        using var provider = Build(s => s.AddBsftScheduling());

        provider.GetRequiredService<IJobScheduleResolver>().Should().BeOfType<JobScheduleResolver>();
        provider.GetRequiredService<IJobHeartbeatStore>().Should().BeOfType<InMemoryJobHeartbeatStore>();
    }

    [Fact]
    public void AJobGetsNamedOptionsOfItsOwnAndIsEnabledByDefault()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftRecurringJob<RecordingJob>(
            JobSchedule.Every(TimeSpan.FromHours(1)), "hourly"));

        provider.GetRequiredService<IOptionsMonitor<BsftJobOptions>>().Get("hourly").Enabled.Should().BeTrue();
        provider.GetRequiredService<IJobRegistry>().IsEnabled("hourly").Should().BeTrue();
    }

    [Fact]
    public void AJobIsSwitchedOffFromItsOwnConfigurationSectionWithoutSkippingItsRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Scheduling:Jobs:hourly:Enabled"] = "false",
                ["Bsft:Scheduling:Jobs:hourly:HeartbeatTolerance"] = "04:00:00",
            })
            .Build();

        using var provider = Build(s => s
            .AddBsftScheduling(configuration)
            .AddBsftRecurringJob<RecordingJob>(JobSchedule.Every(TimeSpan.FromHours(1)), "hourly"));

        var registry = provider.GetRequiredService<IJobRegistry>();

        registry.Find("hourly").Should().NotBeNull("the job stays registered; only its switch is off");
        registry.IsEnabled("hourly").Should().BeFalse();
        provider.GetRequiredService<IOptionsMonitor<BsftJobOptions>>().Get("hourly")
            .HeartbeatTolerance.Should().Be(TimeSpan.FromHours(4));
    }

    [Fact]
    public void ThePerJobDelegateWinsOverThePerJobSectionWhicheverOrderTheCallsCameIn()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bsft:Scheduling:Jobs:hourly:Enabled"] = "false" })
            .Build();

        using var provider = Build(s => s
            .AddBsftRecurringJob<RecordingJob>(
                JobSchedule.Every(TimeSpan.FromHours(1)), "hourly", configure: options => options.Enabled = true)
            .AddBsftScheduling(configuration));

        provider.GetRequiredService<IJobRegistry>().IsEnabled("hourly").Should().BeTrue();
    }

    [Fact]
    public void AJobWithAnUnusableToleranceFailsTheHostRatherThanTheHealthCheck()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftJob<RecordingJob>(
            "report", options => options.HeartbeatToleranceFactor = 0));

        provider.Invoking(p => p.GetRequiredService<IOptionsMonitor<BsftJobOptions>>().Get("report"))
            .Should().Throw<OptionsValidationException>().WithMessage("*HeartbeatToleranceFactor*");
    }

    [Fact]
    public void TheLivenessCheckIsContributedOnceHoweverOftenItIsAskedFor()
    {
        using var provider = Build(s => s
            .AddBsftScheduling()
            .AddBsftSchedulingHealthCheck()
            .AddBsftSchedulingHealthCheck());

        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Should().ContainSingle()
            .Which.Should().Match<HealthCheckRegistration>(registration =>
                registration.Name == "bsft-scheduling-jobs" && registration.Tags.Contains("bsft.scheduling"));
    }

    [Fact]
    public async Task TheContributedLivenessCheckResolvesAndRuns()
    {
        using var provider = Build(s => s
            .AddBsftScheduling()
            .AddBsftSchedulingHealthCheck()
            .AddBsftRecurringJob<RecordingJob>(JobSchedule.Every(TimeSpan.FromHours(1)), "hourly"));

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single();
        var result = await registration.Factory(provider).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("hourly");
    }

    [Fact]
    public void TheLivenessCheckRegistrationRejectsAMissingCollection()
    {
        IServiceCollection services = null!;

        ((Action)(() => services.AddBsftSchedulingHealthCheck()))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
