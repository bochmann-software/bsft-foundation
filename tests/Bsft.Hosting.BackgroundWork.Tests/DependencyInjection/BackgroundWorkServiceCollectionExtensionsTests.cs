using Bsft.Hosting.BackgroundWork;
using Bsft.Hosting.BackgroundWork.DependencyInjection;
using Bsft.Hosting.BackgroundWork.Tests.Fakes;
using Bsft.Hosting.StartGates.DependencyInjection;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.DependencyInjection;

public sealed class BackgroundWorkServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddBsftBackgroundWorkRunOnce_WithAPendingStartGate_AttemptsNothingAndCountsTheStartupDelayFromTheOpening()
    {
        var gate = new ControllableStartGate();
        var clock = new RecordingTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddBsftHostedWorkStartGate(_ => gate);
        services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>(options => options.StartupDelay = TimeSpan.FromSeconds(30));
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<RecordingRunOnceService>();
        var hosted = provider.GetServices<IHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        await gate.Awaited;

        service.AttemptCount.Should().Be(0);
        clock.Delays.Should().BeEmpty();

        gate.Open();
        await service.FirstAttempt.WaitAsync(TimeSpan.FromSeconds(30));

        clock.Delays.Should().Equal(TimeSpan.FromSeconds(30));
        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AddBsftBackgroundWorkRunOnce_StoppedWhileAStartGateIsPending_StopsWithoutAnAttempt()
    {
        var gate = new ControllableStartGate();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftHostedWorkStartGate(_ => gate);
        services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>();
        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        await gate.Awaited;
        await hosted.StopAsync(CancellationToken.None);

        provider.GetRequiredService<RecordingRunOnceService>().AttemptCount.Should().Be(0);
    }

    [Fact]
    public void AddBsftBackgroundWorkRunOnce_RegistersTheServiceAsAHostedServiceWithItsNamedOptions()
    {
        var services = new ServiceCollection();

        services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>(
            options => options.MaxAttempts = 7);

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().ContainSingle()
            .Which.Should().BeOfType<RecordingRunOnceService>();

        provider.GetRequiredService<IOptionsMonitor<BsftRunOnceOptions>>()
            .Get(BsftRunOnceOptions.NameFor<RecordingRunOnceService>())
            .MaxAttempts.Should().Be(7);
    }

    [Fact]
    public void AddBsftBackgroundWorkRunOnce_BindsThePerServiceSection()
    {
        var section = $"{BsftRunOnceOptions.SectionName}:{BsftRunOnceOptions.NameFor<RecordingRunOnceService>()}";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{section}:StartupDelay"] = "00:00:20",
                [$"{section}:MaxAttempts"] = "4",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<BsftRunOnceOptions>>()
            .Get(BsftRunOnceOptions.NameFor<RecordingRunOnceService>());

        options.StartupDelay.Should().Be(TimeSpan.FromSeconds(20));
        options.MaxAttempts.Should().Be(4);
    }

    [Fact]
    public void AddBsftBackgroundWorkRunOnce_TheDelegateRunsAfterTheSectionSoCodeWins()
    {
        var section = $"{BsftRunOnceOptions.SectionName}:{BsftRunOnceOptions.NameFor<RecordingRunOnceService>()}";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{section}:MaxAttempts"] = "4" })
            .Build();

        var services = new ServiceCollection();
        services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>(configuration, options => options.MaxAttempts = 9);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsMonitor<BsftRunOnceOptions>>()
            .Get(BsftRunOnceOptions.NameFor<RecordingRunOnceService>())
            .MaxAttempts.Should().Be(9);
    }

    [Fact]
    public void AddBsftBackgroundWorkRunOnce_AnUnusableScheduleFailsTheHostRatherThanTheFirstBackoff()
    {
        var services = new ServiceCollection();
        services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>(options => options.BackoffFactor = 0);

        using var provider = services.BuildServiceProvider();

        provider.Invoking(p => p.GetRequiredService<IOptionsMonitor<BsftRunOnceOptions>>()
                .Get(BsftRunOnceOptions.NameFor<RecordingRunOnceService>()))
            .Should().Throw<OptionsValidationException>().WithMessage("*BackoffFactor*");
    }

    [Fact]
    public void AddBsftBackgroundWorkRunOnce_RegistersTheDefaultClockWithoutReplacingOne()
    {
        var clock = TimeProvider.System;
        var services = new ServiceCollection();
        services.AddSingleton(clock);

        services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock);
    }

    [Fact]
    public void TheNullGuardsRejectMissingArguments()
    {
        IServiceCollection services = null!;

        ((Action)(() => services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>()))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");

        var configuration = new ConfigurationBuilder().Build();

        ((Action)(() => services.AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>(configuration)))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        ((Action)(() => new ServiceCollection().AddBsftBackgroundWorkRunOnce<RecordingRunOnceService>((IConfiguration)null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }
}
