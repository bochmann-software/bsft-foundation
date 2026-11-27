using Bsft.Tests.Shared;
using Bsft.Core.Tenancy;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
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

    [Fact]
    public async Task AddBsftScheduling_WithABackgroundTenantSource_DispatcherListsOncePerTenant()
    {
        var listedFor = new List<string?>();
        var store = Substitute.For<IJobStore>();
        store.ListClaimableAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lock (listedFor)
                    listedFor.Add(BackgroundTenant.Current);
                return Task.FromResult<IReadOnlyList<JobRun>>([]);
            });

        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var provider = Build(s =>
        {
            s.AddSingleton<IJobStore>(store);
            s.AddSingleton<IBackgroundTenantSource>(new FakeBackgroundTenantSource(["a", "b"]));
            s.AddBsftScheduling();
            s.AddSingleton<TimeProvider>(clock);
        });

        var dispatcher = provider.GetServices<IHostedService>().OfType<JobDispatcher>().Single();
        await RunDispatcherFirstPassAsync(dispatcher, clock);

        Snapshot(listedFor).Take(2).Should().Equal(["a", "b"]);
    }

    [Fact]
    public async Task AddBsftScheduling_WithoutABackgroundTenantSource_DispatcherListsWithoutATenant()
    {
        var listedFor = new List<string?>();
        var store = Substitute.For<IJobStore>();
        store.ListClaimableAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lock (listedFor)
                    listedFor.Add(BackgroundTenant.Current);
                return Task.FromResult<IReadOnlyList<JobRun>>([]);
            });

        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var provider = Build(s =>
        {
            s.AddSingleton<IJobStore>(store);
            s.AddBsftScheduling();
            s.AddSingleton<TimeProvider>(clock);
        });

        var dispatcher = provider.GetServices<IHostedService>().OfType<JobDispatcher>().Single();
        await RunDispatcherFirstPassAsync(dispatcher, clock);

        Snapshot(listedFor).Should().NotBeEmpty().And.OnlyContain(tenant => tenant == null);
    }

    [Fact]
    public async Task AddBsftScheduling_WithATenantScopeFactory_ExecutorUsesTheFactory()
    {
        var createdFor = new List<string>();
        var tenantScopes = Substitute.For<ITenantScopeFactory>();
        var tenantScope = Substitute.For<ITenantScope>();
        var jobProvider = Substitute.For<IServiceProvider>();
        var job = Substitute.For<IJob>();

        tenantScope.ServiceProvider.Returns(jobProvider);
        tenantScopes.CreateScope(Arg.Any<string>())
            .Returns(x =>
            {
                createdFor.Add(x.Arg<string>());
                return tenantScope;
            });

        // Setup CreateScopeAsync to delegate to CreateScope (matching the default interface implementation)
        tenantScopes.CreateScopeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                var scope = tenantScopes.CreateScope(x.Arg<string>());
                return new ValueTask<ITenantScope>(scope);
            });

        jobProvider.GetService(typeof(RecordingJob)).Returns(job);

        using var provider = Build(s =>
        {
            s.AddSingleton(tenantScopes);
            s.AddBsftScheduling().AddBsftJob<RecordingJob>();
        });

        var executor = provider.GetRequiredService<IJobExecutor>();
        var run = new JobRun { Id = 1, JobName = typeof(RecordingJob).FullName!, DueAt = DateTimeOffset.UtcNow, AttemptCount = 0, LeaseExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };

        using (new BackgroundTenant("tenant-x"))
        {
            await executor.ExecuteAsync(typeof(RecordingJob), run);
        }

        createdFor.Should().Equal(["tenant-x"]);
    }

    [Fact]
    public async Task AddBsftScheduling_WithoutATenantScopeFactory_ExecutorUsesAPlainScope()
    {
        using var provider = Build(s => s.AddBsftScheduling().AddBsftJob<RecordingJob>());

        var executor = provider.GetRequiredService<IJobExecutor>();
        var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<RecordingJob>();
        var run = new JobRun { Id = 1, JobName = typeof(RecordingJob).FullName!, DueAt = DateTimeOffset.UtcNow, AttemptCount = 0, LeaseExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };

        using (new BackgroundTenant("tenant-x"))
        {
            await executor.ExecuteAsync(typeof(RecordingJob), run);
        }

        // Should complete without calling a tenant scope factory
    }

    /// <summary>Runs the dispatcher's loop, which is where the passes are split per tenant, until its first pass is done.</summary>
    private static async Task RunDispatcherFirstPassAsync(JobDispatcher dispatcher, ManualTimeProvider clock)
    {
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = dispatcher.RunLoopAsync(stopping.Token);

        // The first pass is complete once the loop waits out its interval on the manual clock.
        await await Task.WhenAny(clock.WaitForAnyTimerAsync(), loop);

        await stopping.CancelAsync();
        await loop;
    }

    private static List<string?> Snapshot(List<string?> list)
    {
        lock (list)
            return [..list];
    }
}
