using Bsft.Retention.DependencyInjection;
using Bsft.Retention.Testing;
using Bsft.Retention.Tests.Fakes;
using Bsft.Retention.Watermarks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Bsft.Hosting.StartGates.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Retention.Tests.Watermarks;

public sealed class DiskWatermarkRegistrationTests
{
    private static readonly string PassName = DiskWatermarkOptions.NameFor<FakeDiskWatermarkSource>();

    [Fact]
    public void NameFor_IsThePassNameOverTheSource()
    {
        PassName.Should().Be(
            "Bsft.Retention.Watermarks.DiskWatermarkRetentionPass<Bsft.Retention.Tests.Fakes.FakeDiskWatermarkSource>");
        PassName.Should().Be(BsftRetentionOptions.NameFor<DiskWatermarkRetentionPass<FakeDiskWatermarkSource>>());
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_RegistersTheSourceThePassAndItsSweeper()
    {
        var services = new ServiceCollection();

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>();

        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(FakeDiskWatermarkSource) && sd.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(DiskWatermarkRetentionPass<FakeDiskWatermarkSource>) && sd.Lifetime == ServiceLifetime.Scoped);
        services.HasBsftGatedHostedService<RetentionSweeper<DiskWatermarkRetentionPass<FakeDiskWatermarkSource>>>().Should().BeTrue();
        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(IVolumeSpaceProvider) && sd.ImplementationType == typeof(VolumeSpaceProvider));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IDiskWatermarkStatusStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IDiskWatermarkStatusReader));
        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(IDiskWatermarkPlanner) && sd.ImplementationType == typeof(DiskWatermarkPlanner));
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_KeepsAVolumeMeasurementRegisteredBefore()
    {
        var fake = new FakeVolumeSpaceProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IVolumeSpaceProvider>(fake);

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVolumeSpaceProvider>().Should().BeSameAs(fake);
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_AppliesBothConfigureActionsUnderThePassName()
    {
        var services = new ServiceCollection();

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(
            watermark => watermark.MaxArchiveBytes = 1_000,
            retention => retention.RetentionPeriod = TimeSpan.FromMinutes(10));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<DiskWatermarkOptions>>().Get(PassName).MaxArchiveBytes.Should().Be(1_000);
        provider.GetRequiredService<IOptionsMonitor<BsftRetentionOptions>>().Get(PassName).RetentionPeriod
            .Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_AbsoluteExpiryOnTheSweep_FailsOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(
            watermark => watermark.MaxArchiveBytes = 1_000,
            retention => retention.UseAbsoluteExpiry = true);

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<BsftRetentionOptions>>().Get(PassName);

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{PassName}*UseAbsoluteExpiry*");
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_BindsBothOptionsFromOneSectionAndLetsCodeWin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Bsft:Retention:{PassName}:RetentionPeriod"] = "00:15:00",
                [$"Bsft:Retention:{PassName}:BatchSize"] = "7",
                [$"Bsft:Retention:{PassName}:MaxArchiveBytes"] = "5000",
                [$"Bsft:Retention:{PassName}:TargetFillRatio"] = "0.75",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(
            configuration,
            watermark => watermark.TargetFillRatio = 0.5,
            retention => retention.BatchSize = 3);

        using var provider = services.BuildServiceProvider();
        var watermark = provider.GetRequiredService<IOptionsMonitor<DiskWatermarkOptions>>().Get(PassName);
        var retention = provider.GetRequiredService<IOptionsMonitor<BsftRetentionOptions>>().Get(PassName);

        watermark.MaxArchiveBytes.Should().Be(5_000);
        watermark.TargetFillRatio.Should().Be(0.5);
        retention.RetentionPeriod.Should().Be(TimeSpan.FromMinutes(15));
        retention.BatchSize.Should().Be(3);
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_ConfigurationWithoutDelegates_BindsTheSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Bsft:Retention:{PassName}:MinAvailableBytes"] = "100",
                [$"Bsft:Retention:{PassName}:VolumePath"] = "/var/archive",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(configuration);

        using var provider = services.BuildServiceProvider();
        var watermark = provider.GetRequiredService<IOptionsMonitor<DiskWatermarkOptions>>().Get(PassName);
        watermark.MinAvailableBytes.Should().Be(100);
        watermark.VolumePath.Should().Be("/var/archive");
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_ConfigurationWithoutRetentionDelegate_BindsTheRetentionSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Bsft:Retention:{PassName}:RetentionPeriod"] = "00:15:00",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<BsftRetentionOptions>>().Get(PassName).RetentionPeriod
            .Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_WithoutAnyDelegate_ResolvesTheDefaults()
    {
        var services = new ServiceCollection();

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>();

        using var provider = services.BuildServiceProvider();
        var watermark = provider.GetRequiredService<IOptionsMonitor<DiskWatermarkOptions>>().Get(PassName);
        var retention = provider.GetRequiredService<IOptionsMonitor<BsftRetentionOptions>>().Get(PassName);

        watermark.MaxArchiveBytes.Should().BeNull();
        watermark.TargetFillRatio.Should().Be(new DiskWatermarkOptions().TargetFillRatio);
        retention.RetentionPeriod.Should().BeNull();
    }

    [Fact]
    public void AddBsftRetentionDiskWatermark_InvalidWatermark_FailsValidation()
    {
        var services = new ServiceCollection();

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(watermark => watermark.MaxArchiveBytes = 0);

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<DiskWatermarkOptions>>().Get(PassName);

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{PassName}*");
    }

    [Fact]
    public async Task AddBsftRetentionDiskWatermark_TheRegisteredSweeperDrainsTheArchive()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeDiskWatermarkSource()
            .Add("oldest", 600, now.AddDays(-3))
            .Add("older", 600, now.AddDays(-2))
            .Add("newest", 600, now.AddMinutes(-1));
        var services = new ServiceCollection();
        services.AddScoped(_ => source);

        services.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(
            watermark => watermark.MaxArchiveBytes = 1_000,
            retention => retention.RetentionPeriod = TimeSpan.FromHours(1));

        using var provider = services.BuildServiceProvider();
        var sweeper = provider.GetServices<IHostedService>()
            .OfType<RetentionSweeper<DiskWatermarkRetentionPass<FakeDiskWatermarkSource>>>()
            .Single();

        await sweeper.SweepAsync(TimeSpan.FromHours(1));

        // 1800 bytes against a target of 900: the two older items go, the newest is too young to.
        source.Deleted.Select(item => item.Key).Should().Equal("oldest", "older");
        provider.GetRequiredService<IDiskWatermarkStatusReader>().Find(PassName)!.State
            .Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var configuration = new ConfigurationBuilder().Build();

        var calls = new Action[]
        {
            () => RetentionServiceCollectionExtensions.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(null!),
            () => RetentionServiceCollectionExtensions.AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>(null!, configuration),
            () => new ServiceCollection().AddBsftRetentionDiskWatermark<FakeDiskWatermarkSource>((IConfiguration)null!),
        };

        calls.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
    }
}
