using Bsft.Core.Hosting;
using Bsft.Outbox.DependencyInjection;
using Bsft.Outbox.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Outbox.Tests.DependencyInjection;

/// <summary>Tests what registering the optional hosted drain worker gives a host.</summary>
public sealed class OutboxDrainWorkerRegistrationTests
{
    [Fact]
    public void AddBsftOutbox_WithoutTheWorker_RegistersNoHostedService()
    {
        using var provider = Provider(services => services.AddBsftOutbox<string>("primary"));

        provider.GetServices<IHostedService>().Should().BeEmpty();
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_RegistersTheWorkerAsAHostedServiceAndTheKernelBeneathIt()
    {
        using var provider = Provider(services => services.AddBsftOutboxDrainWorker<string>("primary"));

        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<OutboxDrainWorker<string>>();
        provider.GetRequiredService<IOutboxDrainer<string>>().Should().NotBeNull();
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_WhenCalledTwiceForOneOutbox_RegistersOneWorker()
    {
        using var provider = Provider(services =>
        {
            services.AddBsftOutboxDrainWorker<string>("primary");
            services.AddBsftOutboxDrainWorker<string>("primary");
        });

        provider.GetServices<IHostedService>().Should().ContainSingle();
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_ForTwoOutboxesOfDifferentPayloads_RegistersAWorkerEach()
    {
        using var provider = Provider(services =>
        {
            services.AddBsftOutboxDrainWorker<string>("primary");
            services.AddBsftOutboxDrainWorker<int>("secondary");
        });

        provider.GetServices<IHostedService>().Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_ForASecondNameOfTheSamePayload_IsRefused()
    {
        var services = new ServiceCollection();
        services.AddBsftOutboxDrainWorker<string>("primary");

        var act = () => services.AddBsftOutboxDrainWorker<string>("secondary");

        act.Should().Throw<InvalidOperationException>().WithMessage("*String*one to one*secondary*");
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_AfterTheKernelWasRegisteredUnderAnotherName_IsRefused()
    {
        var services = new ServiceCollection();
        services.AddBsftOutbox<string>("primary");

        var act = () => services.AddBsftOutboxDrainWorker<string>("secondary");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_ForTheSameNameAfterTheKernel_IsAccepted()
    {
        using var provider = Provider(services =>
        {
            services.AddBsftOutbox<string>("primary");
            services.AddBsftOutboxDrainWorker<string>("primary");
        });

        provider.GetServices<IHostedService>().Should().ContainSingle();
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_WithAGraceBeyondTheHostShutdownTimeout_IsRefusedBeforeTheHostStarts()
    {
        using var provider = Provider(services =>
        {
            services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(3));
            services.AddBsftOutboxDrainWorker<string>(
                "primary", configureWorker: options => options.ShutdownGrace = TimeSpan.FromSeconds(3));
        });

        var act = () => provider.GetRequiredService<IOptionsMonitor<BsftOutboxDrainWorkerOptions>>().Get("primary");

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{nameof(HostOptions.ShutdownTimeout)}*");
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_BindsTheOptionsOfThatOutboxByName()
    {
        using var provider = Provider(services => services.AddBsftOutboxDrainWorker<string>(
            "primary",
            options => options.PollInterval = TimeSpan.FromSeconds(9),
            options => options.ShutdownGrace = TimeSpan.FromSeconds(3)));

        provider.GetRequiredService<IOptionsMonitor<BsftOutboxOptions>>().Get("primary")
            .PollInterval.Should().Be(TimeSpan.FromSeconds(9));
        provider.GetRequiredService<IOptionsMonitor<BsftOutboxDrainWorkerOptions>>().Get("primary")
            .ShutdownGrace.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_WithConfiguration_BindsBothSectionsAndLetsTheActionsWin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftOutboxOptions.SectionName}:primary:BatchSize"] = "7",
                [$"{BsftOutboxOptions.SectionName}:primary:PollInterval"] = "00:00:09",
                [$"{BsftOutboxDrainWorkerOptions.SectionName}:primary:ShutdownGrace"] = "00:00:02",
            })
            .Build();

        using var provider = Provider(services => services.AddBsftOutboxDrainWorker<string>(
            "primary", configuration, options => options.BatchSize = 3, options => options.ShutdownGrace += TimeSpan.FromSeconds(1)));

        var outbox = provider.GetRequiredService<IOptionsMonitor<BsftOutboxOptions>>().Get("primary");
        outbox.BatchSize.Should().Be(3);
        outbox.PollInterval.Should().Be(TimeSpan.FromSeconds(9));
        provider.GetRequiredService<IOptionsMonitor<BsftOutboxDrainWorkerOptions>>().Get("primary")
            .ShutdownGrace.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_WithAnUnusableGrace_IsRefusedBeforeTheHostStarts()
    {
        using var provider = Provider(services => services.AddBsftOutboxDrainWorker<string>(
            "primary", configureWorker: options => options.ShutdownGrace = TimeSpan.FromSeconds(-1)));

        var act = () => provider.GetRequiredService<IOptionsMonitor<BsftOutboxDrainWorkerOptions>>().Get("primary");

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{nameof(BsftOutboxDrainWorkerOptions.ShutdownGrace)}*");
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_WithAnUnworkableInterval_IsRefusedBeforeTheHostStarts()
    {
        using var provider = Provider(services => services.AddBsftOutboxDrainWorker<string>(
            "primary", options => options.PollInterval = TimeSpan.Zero));

        var act = () => provider.GetRequiredService<IOptionsMonitor<BsftOutboxOptions>>().Get("primary");

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{nameof(BsftOutboxOptions.PollInterval)}*");
    }

    [Fact]
    public async Task TheWorker_WhenAKeyedSignalIsRegisteredUnderTheOutboxName_WakesOnIt()
    {
        var signal = new ManualWakeSignal();
        using var provider = Provider(services =>
        {
            services.AddKeyedSingleton<IOutboxWakeSignal>("primary", signal);
            services.AddBsftOutboxDrainWorker<string>("primary", options => options.PollInterval = TimeSpan.FromHours(1));
        });
        var store = (RecordingOutboxStore)provider.GetRequiredService<IOutboxStore<string>>();

        await WaitForLeasesAsync(provider, store, expected: 1);
        signal.Raise();
        await WaitForLeasesAsync(provider, store, expected: 2, startHost: false);
    }

    [Fact]
    public async Task TheWorker_WhenAnUnkeyedSignalIsRegistered_WakesOnIt()
    {
        var signal = new ManualWakeSignal();
        using var provider = Provider(services =>
        {
            services.AddSingleton<IOutboxWakeSignal>(signal);
            services.AddBsftOutboxDrainWorker<string>("primary", options => options.PollInterval = TimeSpan.FromHours(1));
        });
        var store = (RecordingOutboxStore)provider.GetRequiredService<IOutboxStore<string>>();

        await WaitForLeasesAsync(provider, store, expected: 1);
        signal.Raise();
        await WaitForLeasesAsync(provider, store, expected: 2, startHost: false);
    }

    [Fact]
    public async Task TheWorker_WhenAStartGateIsRegistered_WaitsForIt()
    {
        var gate = new Bsft.Tests.Shared.ControllableStartGate();
        using var provider = Provider(services =>
        {
            services.AddSingleton<IHostedWorkStartGate>(gate);
            services.AddBsftOutboxDrainWorker<string>("primary");
        });
        var store = (RecordingOutboxStore)provider.GetRequiredService<IOutboxStore<string>>();
        var worker = provider.GetServices<IHostedService>().Single();

        await worker.StartAsync(CancellationToken.None);
        await gate.Awaited;

        store.Leases.Should().BeEmpty();

        gate.Open();
        await WaitForLeasesAsync(provider, store, expected: 1, startHost: false);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void AddBsftOutboxDrainWorker_WithAMissingArgument_Throws()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        var cases = new (string Parameter, Action Call)[]
        {
            ("services", () => OutboxServiceCollectionExtensions.AddBsftOutboxDrainWorker<string>(null!, "primary")),
            ("name", () => new ServiceCollection().AddBsftOutboxDrainWorker<string>(null!)),
            ("services", () => OutboxServiceCollectionExtensions.AddBsftOutboxDrainWorker<string>(null!, "primary", configuration)),
            ("name", () => new ServiceCollection().AddBsftOutboxDrainWorker<string>(null!, configuration)),
            ("configuration", () => new ServiceCollection().AddBsftOutboxDrainWorker<string>("primary", (IConfiguration)null!)),
        };

        foreach (var (parameter, call) in cases)
            call.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }

    private static async Task WaitForLeasesAsync(
        ServiceProvider provider, RecordingOutboxStore store, int expected, bool startHost = true)
    {
        if (startHost)
            await provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (store.Leases.Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(5);

        store.Leases.Count.Should().BeGreaterThanOrEqualTo(expected);

        if (expected > 1)
            await provider.GetServices<IHostedService>().Single().StopAsync(CancellationToken.None);
    }

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IOutboxStore<string>>(new RecordingOutboxStore());
        services.AddSingleton<IOutboxSideEffect<string>>(new ScriptedSideEffect());
        services.AddSingleton<IOutboxStore<int>>(new EmptyStore());
        services.AddSingleton<IOutboxSideEffect<int>>(new NoSideEffect());

        configure(services);

        return services.BuildServiceProvider();
    }

    private sealed class EmptyStore : IOutboxStore<int>
    {
        public Task<IReadOnlyList<LeasedOutboxRow<int>>> LeaseAsync(
            DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LeasedOutboxRow<int>>>([]);

        public Task<bool> TryMarkProcessedAsync(
            long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken ct = default) => Task.FromResult(true);

        public Task AbandonAsync(long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeadLetterAsync(
            long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoSideEffect : IOutboxSideEffect<int>
    {
        public Task ApplyAsync(int payload, CancellationToken ct = default) => Task.CompletedTask;

        public string Describe(int payload) => payload.ToString();
    }
}
