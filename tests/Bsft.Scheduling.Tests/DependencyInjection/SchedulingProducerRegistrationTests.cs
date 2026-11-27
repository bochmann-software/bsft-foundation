using Bsft.Scheduling.DependencyInjection;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.StoredSchedules;
using Bsft.Scheduling.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Scheduling.Tests.DependencyInjection;

public class SchedulingProducerRegistrationTests
{
    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobLog());
        configure(services);
        return services.BuildServiceProvider();
    }

    private static List<Type> HostedTypes(ServiceProvider provider) =>
        provider.GetServices<IHostedService>().Select(h => h.GetType()).ToList();

    [Fact]
    public void AddBsftSchedulingProducer_WhenRegistered_AddsNoHostedService()
    {
        using var provider = Build(s => s.AddBsftSchedulingProducer());

        provider.GetServices<IHostedService>().Should().BeEmpty();
    }

    [Fact]
    public async Task AddBsftSchedulingProducer_WhenAJobIsEnqueued_StoresARunWithoutClaimingIt()
    {
        var store = new InMemoryJobStore();
        using var provider = Build(s =>
        {
            s.AddSingleton<IJobStore>(store);
            s.AddBsftSchedulingProducer();
            s.AddBsftJob<RecordingJob>("record");
        });
        using var scope = provider.CreateScope();

        var id = await scope.ServiceProvider.GetRequiredService<IJobScheduler>()
            .EnqueueAsync("record", TimeSpan.Zero, "payload");

        var run = await store.FindAsync(id);
        run.Should().NotBeNull();
        run!.Status.Should().Be(JobRunStatus.Pending);
        provider.GetRequiredService<JobLog>().Count.Should().Be(0);
        provider.GetServices<IHostedService>().Should().BeEmpty();
    }

    [Fact]
    public async Task AddBsftSchedulingProducer_WhenTheStoreIsCountedAfterAnEnqueue_NoRunWasClaimed()
    {
        var store = Substitute.For<IJobStore>();
        store.EnqueueAsync(Arg.Any<JobRun>(), Arg.Any<CancellationToken>()).Returns(1L);
        using var provider = Build(s =>
        {
            s.AddSingleton(store);
            s.AddBsftSchedulingProducer();
            s.AddBsftJob<RecordingJob>("record");
        });
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IJobScheduler>().EnqueueAsync("record", TimeSpan.Zero);

        await store.DidNotReceiveWithAnyArgs().ClaimAsync(default!, default!, default, default);
        await store.DidNotReceiveWithAnyArgs().SettleAsync(default!, default, default);
        await store.DidNotReceiveWithAnyArgs().ListClaimableAsync(default, default);
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenRegistered_ResolvesTheStoreAndTheSchedulerServices()
    {
        using var provider = Build(s => s.AddBsftSchedulingProducer());
        using var scope = provider.CreateScope();

        provider.GetRequiredService<IJobStore>().Should().BeOfType<InMemoryJobStore>();
        provider.GetRequiredService<IJobRegistry>().Should().NotBeNull();
        provider.GetRequiredService<IStoredScheduleService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IJobScheduler>().Should().BeOfType<JobScheduler>();
    }

    [Fact]
    public void AddBsftScheduling_WhenRegistered_StillAddsTheSeedersAndTheDispatcherInOrder()
    {
        using var provider = Build(s => s.AddBsftScheduling());

        HostedTypes(provider).Should().Equal(
            typeof(RecurringJobSeeder), typeof(StoredScheduleSeeder), typeof(JobDispatcher));
    }

    [Fact]
    public void AddBsftScheduling_WhenRegisteredWithConfiguration_StillAddsTheHostedServices()
    {
        using var provider = Build(s => s.AddBsftScheduling(new ConfigurationBuilder().Build()));

        HostedTypes(provider).Should().Equal(
            typeof(RecurringJobSeeder), typeof(StoredScheduleSeeder), typeof(JobDispatcher));
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenFollowedByTheFullRegistration_YieldsTheFullHost()
    {
        using var provider = Build(s =>
        {
            s.AddBsftSchedulingProducer();
            s.AddBsftScheduling();
        });

        HostedTypes(provider).Should().Equal(
            typeof(RecurringJobSeeder), typeof(StoredScheduleSeeder), typeof(JobDispatcher));
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenPrecededByTheFullRegistration_IsIdempotent()
    {
        using var provider = Build(s =>
        {
            s.AddBsftScheduling();
            s.AddBsftSchedulingProducer();
        });

        HostedTypes(provider).Should().Equal(
            typeof(RecurringJobSeeder), typeof(StoredScheduleSeeder), typeof(JobDispatcher));
        provider.GetServices<IValidateOptions<BsftSchedulingOptions>>().Should().ContainSingle();
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenTheOptionsActionIsGiven_AppliesIt()
    {
        using var provider = Build(s => s.AddBsftSchedulingProducer(o => o.BatchSize = 7));

        provider.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value.BatchSize.Should().Be(7);
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenConfigurationIsGiven_BindsTheSectionAndLetsCodeWin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Scheduling:BatchSize"] = "11",
                ["Bsft:Scheduling:InstanceId"] = "api",
            })
            .Build();

        using var provider = Build(s => s.AddBsftSchedulingProducer(configuration, o => o.BatchSize = 12));

        var options = provider.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value;
        options.BatchSize.Should().Be(12);
        options.InstanceId.Should().Be("api");
        provider.GetServices<IHostedService>().Should().BeEmpty();
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenCombinedWithTheFullRegistration_RunsBothOptionsDelegatesInCallOrder()
    {
        var calls = new List<string>();

        using var provider = Build(s =>
        {
            s.AddBsftSchedulingProducer(o => { calls.Add("producer"); o.BatchSize = 3; o.MaxAttempts = 4; });
            s.AddBsftScheduling(o => { calls.Add("full"); o.BatchSize = 9; });
        });

        var options = provider.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value;
        calls.Should().Equal("producer", "full");
        options.BatchSize.Should().Be(9);
        options.MaxAttempts.Should().Be(4);
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenConfigurationIsGiven_BindsThePerJobOptionsLikeTheFullOverload()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bsft:Scheduling:Jobs:record:Enabled"] = "false" })
            .Build();

        using var provider = Build(s =>
        {
            s.AddBsftSchedulingProducer(configuration);
            s.AddBsftJob<RecordingJob>("record");
        });

        provider.GetRequiredService<IOptionsMonitor<BsftJobOptions>>().Get("record").Enabled.Should().BeFalse();
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenAnOptionIsInvalid_FailsOptionsValidation()
    {
        using var provider = Build(s => s.AddBsftSchedulingProducer(o => o.LeaseDuration = TimeSpan.Zero));

        provider.Invoking(p => p.GetRequiredService<IOptions<BsftSchedulingOptions>>().Value)
            .Should().Throw<OptionsValidationException>().WithMessage("*LeaseDuration*");
    }

    [Fact]
    public void AddBsftSchedulingProducer_WhenAnArgumentIsMissing_Throws()
    {
        ((Action)(() => SchedulingServiceCollectionExtensions.AddBsftSchedulingProducer(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        ((Action)(() => SchedulingServiceCollectionExtensions.AddBsftSchedulingProducer(null!, new ConfigurationBuilder().Build())))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        ((Action)(() => new ServiceCollection().AddBsftSchedulingProducer((IConfiguration)null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }
}
