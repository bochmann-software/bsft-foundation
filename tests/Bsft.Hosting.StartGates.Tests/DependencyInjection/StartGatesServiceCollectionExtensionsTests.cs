using Bsft.Core.Hosting;
using Bsft.Hosting.StartGates.DependencyInjection;
using Bsft.Hosting.StartGates.Tests.Fakes;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bsft.Hosting.StartGates.Tests.DependencyInjection;

public sealed class StartGatesServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AddBsftGatedHostedService_WithoutAGate_HandsTheServiceItselfToTheHost()
    {
        using var host = Build(services => services.AddBsftGatedHostedService<RecordingLoop>());

        await host.StartAsync();

        var hosted = host.Services.GetServices<IHostedService>().Single();
        hosted.Should().BeSameAs(host.Services.GetRequiredService<RecordingLoop>());
        await host.Services.GetRequiredService<RecordingLoop>().Running.WaitAsync(Patience);
        host.Services.GetRequiredService<RecordingLoop>().Starts.Should().Be(1);
        await host.StopAsync();
    }

    [Fact]
    public async Task AddBsftGatedHostedService_WithAPendingGate_StartsTheHostAndHoldsTheServiceBack()
    {
        var gate = new ControllableStartGate();
        using var host = Build(services =>
        {
            services.AddBsftHostedWorkStartGate(_ => gate);
            services.AddBsftGatedHostedService<RecordingLoop>();
        });

        await host.StartAsync();
        await gate.Awaited;

        host.Services.GetRequiredService<RecordingLoop>().Starts.Should().Be(0);

        gate.Open();
        await host.Services.GetRequiredService<RecordingLoop>().Running.WaitAsync(Patience);
        await host.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddBsftGatedHostedService_WhenTheGateIsRegisteredAfterTheLoops_StillGatesThem(bool gateFirst)
    {
        var gate = new ControllableStartGate();
        using var host = Build(services =>
        {
            if (gateFirst)
                services.AddBsftHostedWorkStartGate(_ => gate);

            services.AddBsftGatedHostedService<RecordingLoop>();

            if (!gateFirst)
                services.AddBsftHostedWorkStartGate(_ => gate);
        });

        await host.StartAsync();
        await gate.Awaited;

        host.Services.GetRequiredService<RecordingLoop>().Starts.Should().Be(0);
        gate.Open();
        await host.Services.GetRequiredService<RecordingLoop>().Running.WaitAsync(Patience);
        await host.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddBsftGatedHostedService_WhateverServicesStartConcurrentlySays_DoesNotBlockTheHostStart(bool concurrently)
    {
        var gate = new ControllableStartGate();
        var plain = new PlainHostedService();
        using var host = Build(services =>
        {
            services.Configure<HostOptions>(options => options.ServicesStartConcurrently = concurrently);
            services.AddBsftHostedWorkStartGate(_ => gate);
            services.AddBsftGatedHostedService<RecordingLoop>();
            services.AddSingleton<IHostedService>(plain);
        });

        await host.StartAsync().WaitAsync(Patience);

        plain.Starts.Should().Be(1);
        host.Services.GetRequiredService<RecordingLoop>().Starts.Should().Be(0);
        await host.StopAsync();
    }

    [Fact]
    public async Task AddBsftGatedHostedService_ForAServiceRegisteredWithAddHostedService_LeavesItUngated()
    {
        var gate = new ControllableStartGate();
        var plain = new PlainHostedService();
        using var host = Build(services =>
        {
            services.AddBsftHostedWorkStartGate(_ => gate);
            services.AddHostedService(_ => plain);
            services.AddBsftGatedHostedService<RecordingLoop>();
        });

        await host.StartAsync();

        plain.Starts.Should().Be(1);
        host.Services.GetRequiredService<RecordingLoop>().Starts.Should().Be(0);
        await host.StopAsync();
    }

    [Fact]
    public async Task AddBsftGatedHostedService_WhenTheHostStopsWhileTheGateIsPending_StopsWithoutStoppingTheService()
    {
        var gate = new ControllableStartGate();
        using var host = Build(services =>
        {
            services.AddBsftHostedWorkStartGate(_ => gate);
            services.AddBsftGatedHostedService<RecordingLoop>();
        });
        await host.StartAsync();
        await gate.Awaited;

        await host.StopAsync();

        var loop = host.Services.GetRequiredService<RecordingLoop>();
        loop.Starts.Should().Be(0);
        loop.Stops.Should().Be(0);
    }

    [Fact]
    public async Task AddBsftGatedHostedService_WhenAGateFaultsAndTheHostStops_StopsTheHostWithoutStartingTheService()
    {
        using var host = Build(services =>
        {
            services.AddBsftHostedWorkStartGate<FaultingGate>();
            services.AddBsftGatedHostedService<RecordingLoop>();
        });
        var stopping = new TaskCompletionSource();
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());

        await host.StartAsync();

        await stopping.Task.WaitAsync(Patience);
        host.Services.GetRequiredService<RecordingLoop>().Starts.Should().Be(0);
    }

    [Fact]
    public async Task AddBsftGatedHostedService_WhenAGateFaultsAndTheBehaviorIsIgnore_KeepsTheHostRunning()
    {
        var stopped = false;
        using var host = Build(services =>
        {
            services.Configure<HostOptions>(options => options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
            services.AddBsftHostedWorkStartGate<FaultingGate>();
            services.AddBsftGatedHostedService<RecordingLoop>();
        });
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped = true);

        await host.StartAsync();
        await Task.Delay(100);

        stopped.Should().BeFalse();
        await host.StopAsync();
    }

    [Fact]
    public async Task AddBsftGatedHostedService_WhenTheInnerLoopFaultsAfterTheGates_StopsTheHost()
    {
        using var host = Build(services =>
        {
            services.AddBsftHostedWorkStartGate<OpenGate>();
            services.AddBsftGatedHostedService(_ => new RecordingLoop { Failure = new InvalidOperationException("the loop broke") });
        });
        var stopping = new TaskCompletionSource();
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());

        await host.StartAsync();

        await stopping.Task.WaitAsync(Patience);
    }

    [Fact]
    public void AddBsftGatedHostedService_CalledTwiceForOneType_RegistersItOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftGatedHostedService<RecordingLoop>();
        services.AddBsftGatedHostedService<RecordingLoop>();
        services.AddBsftGatedHostedService<OtherRecordingLoop>();

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(2);
    }

    [Fact]
    public void AddBsftGatedHostedService_ForAServiceBehindAFactoryCalledTwice_RegistersItOnceWhenTheTypedOverloadIsUsed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingLoop>();

        // What a registration method that runs twice does when it names the service type.
        services.AddBsftGatedHostedService<RecordingLoop>();
        services.AddBsftGatedHostedService<RecordingLoop>();

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(RecordingLoop)).Should().Be(1);
    }

    [Fact]
    public async Task AddBsftGatedHostedService_WithAFactory_GatesTheServiceItBuilds()
    {
        var gate = new ControllableStartGate();
        var plain = new PlainHostedService();
        using var host = Build(services =>
        {
            services.AddBsftHostedWorkStartGate(_ => gate);
            services.AddBsftGatedHostedService(_ => plain);
        });

        await host.StartAsync();
        await gate.Awaited;
        plain.Starts.Should().Be(0);

        gate.Open();
        await WaitUntilAsync(() => plain.Starts == 1);
        await host.StopAsync();
        plain.Stops.Should().Be(1);
    }

    [Fact]
    public void AddBsftHostedWorkStartGate_ForTheSameTypeTwice_RegistersItOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftHostedWorkStartGate<OpenGate>();
        services.AddBsftHostedWorkStartGate<OpenGate>();

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedWorkStartGate)).Should().Be(1);
    }

    [Fact]
    public void HasBsftGatedHostedService_AfterTheTypedRegistration_IsTrueForThatTypeOnly()
    {
        var services = new ServiceCollection();
        services.AddHostedService<PlainHostedService>();

        services.HasBsftGatedHostedService<RecordingLoop>().Should().BeFalse();
        services.HasBsftGatedHostedService<PlainHostedService>().Should().BeFalse("AddHostedService does not gate");

        services.AddBsftGatedHostedService<RecordingLoop>();

        services.HasBsftGatedHostedService<RecordingLoop>().Should().BeTrue();
        services.HasBsftGatedHostedService<OtherRecordingLoop>().Should().BeFalse();
    }

    [Fact]
    public void RemoveBsftGatedHostedService_AfterTheTypedRegistration_RemovesTheHostedServiceAndItsOwnRegistration()
    {
        var services = new ServiceCollection();
        services.AddBsftGatedHostedService<RecordingLoop>();
        services.AddBsftGatedHostedService<OtherRecordingLoop>();

        services.RemoveBsftGatedHostedService<RecordingLoop>().Should().BeSameAs(services);

        services.HasBsftGatedHostedService<RecordingLoop>().Should().BeFalse();
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(RecordingLoop));
        services.HasBsftGatedHostedService<OtherRecordingLoop>().Should().BeTrue();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(OtherRecordingLoop));
    }

    [Fact]
    public void RemoveBsftGatedHostedService_ForAServiceThatWasNotRegisteredThatWay_ChangesNothing()
    {
        var services = new ServiceCollection();
        services.AddHostedService<PlainHostedService>();
        services.AddSingleton<RecordingLoop>();

        services.RemoveBsftGatedHostedService<RecordingLoop>();

        services.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveBsftGatedHostedService_WhenTheServiceWasRegisteredUnderItsOwnFactory_KeepsThatRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new RecordingLoop());
        services.AddBsftGatedHostedService<RecordingLoop>();

        services.RemoveBsftGatedHostedService<RecordingLoop>();

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(RecordingLoop));
    }

    [Fact]
    public void Methods_WithAMissingArgument_Throw()
    {
        var services = new ServiceCollection();
        var cases = new (string Parameter, Action Call)[]
        {
            ("services", () => StartGatesServiceCollectionExtensions.AddBsftHostedWorkStartGate<OpenGate>(null!)),
            ("services", () => StartGatesServiceCollectionExtensions.AddBsftHostedWorkStartGate<OpenGate>(null!, _ => new OpenGate())),
            ("factory", () => services.AddBsftHostedWorkStartGate<OpenGate>(null!)),
            ("services", () => StartGatesServiceCollectionExtensions.AddBsftGatedHostedService<RecordingLoop>(null!)),
            ("services", () => StartGatesServiceCollectionExtensions.AddBsftGatedHostedService(null!, _ => new RecordingLoop())),
            ("factory", () => services.AddBsftGatedHostedService(null!)),
            ("services", () => StartGatesServiceCollectionExtensions.HasBsftGatedHostedService<RecordingLoop>(null!)),
            ("services", () => StartGatesServiceCollectionExtensions.RemoveBsftGatedHostedService<RecordingLoop>(null!)),
        };

        foreach (var (parameter, call) in cases)
            call.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }

    private static IHost Build(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        configure(builder.Services);

        return builder.Build();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        condition().Should().BeTrue();
    }

    private sealed class OpenGate : IHostedWorkStartGate
    {
        public Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
