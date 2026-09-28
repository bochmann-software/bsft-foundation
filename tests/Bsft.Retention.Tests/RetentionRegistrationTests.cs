using Bsft.Retention.DependencyInjection;
using Bsft.Retention.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Retention.Tests;

public sealed class RetentionRegistrationTests
{
    private static BsftRetentionOptions Resolve(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptionsMonitor<BsftRetentionOptions>>()
            .Get(BsftRetentionOptions.NameFor<RecordingRetentionPass>());
    }

    [Fact]
    public void AddBsftRetention_RegistersThePassAndItsSweeper()
    {
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>();

        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(RecordingRetentionPass) && sd.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(IHostedService)
            && sd.ImplementationType == typeof(RetentionSweeper<RecordingRetentionPass>));
    }

    [Fact]
    public void AddBsftRetention_NeedsNoScheduler()
    {
        // A sweep is its own hosted loop and needs nothing else registered: one call is a whole
        // working retention pass, with no scheduler and no store behind it.
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().Should()
            .ContainSingle(service => service is RetentionSweeper<RecordingRetentionPass>);
    }

    [Fact]
    public void AddBsftRetention_AppliesTheConfigureAction()
    {
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>(o =>
        {
            o.RetentionPeriod = TimeSpan.FromDays(7);
            o.BatchSize = 42;
        });

        var options = Resolve(services);
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(7));
        options.BatchSize.Should().Be(42);
    }

    [Fact]
    public void AddBsftRetention_WithoutConfigure_LeavesTheDefaults()
    {
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>();

        var options = Resolve(services);
        options.RetentionPeriod.Should().BeNull();
        options.BatchSize.Should().Be(500);
        options.MaxBatchesPerSweep.Should().Be(100);
        options.SweepInterval.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void AddBsftRetention_RegistersTheValidatorOnlyOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>();
        services.AddBsftRetention<SecondRetentionPass>();

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IValidateOptions<BsftRetentionOptions>));
    }

    [Fact]
    public void AddBsftRetention_TwoPasses_AreConfiguredIndependently()
    {
        // The named options are what keep several passes apart, each with its own window.
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>(o => o.RetentionPeriod = TimeSpan.FromDays(7));
        services.AddBsftRetention<SecondRetentionPass>(o => o.RetentionPeriod = TimeSpan.FromDays(90));

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<BsftRetentionOptions>>();

        monitor.Get(BsftRetentionOptions.NameFor<RecordingRetentionPass>()).RetentionPeriod
            .Should().Be(TimeSpan.FromDays(7));
        monitor.Get(BsftRetentionOptions.NameFor<SecondRetentionPass>()).RetentionPeriod
            .Should().Be(TimeSpan.FromDays(90));
        provider.GetServices<IHostedService>().Should().HaveCount(2);
    }

    [Fact]
    public void AddBsftRetention_BindsThePerPassConfigurationSection()
    {
        var section = $"{BsftRetentionOptions.SectionName}:{BsftRetentionOptions.NameFor<RecordingRetentionPass>()}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{section}:RetentionPeriod"] = "30.00:00:00",
                [$"{section}:BatchSize"] = "250"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>(configuration);

        var options = Resolve(services);
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(30));
        options.BatchSize.Should().Be(250);
    }

    [Fact]
    public void AddBsftRetention_ConfigureWinsOverConfiguration()
    {
        var section = $"{BsftRetentionOptions.SectionName}:{BsftRetentionOptions.NameFor<RecordingRetentionPass>()}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{section}:BatchSize"] = "250" })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>(configuration, o => o.BatchSize = 7);

        Resolve(services).BatchSize.Should().Be(7);
    }

    [Fact]
    public void AddBsftRetention_InvalidOptions_FailValidation()
    {
        var services = new ServiceCollection();

        services.AddBsftRetention<RecordingRetentionPass>(o => o.BatchSize = 0);

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<BsftRetentionOptions>>()
            .Get(BsftRetentionOptions.NameFor<RecordingRetentionPass>());

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddBsftRetention_NullServices_Throws()
    {
        var act = () => RetentionServiceCollectionExtensions
            .AddBsftRetention<RecordingRetentionPass>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftRetention_Configuration_NullServices_Throws()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => RetentionServiceCollectionExtensions
            .AddBsftRetention<RecordingRetentionPass>(null!, configuration);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftRetention_NullConfiguration_Throws()
    {
        var act = () => new ServiceCollection()
            .AddBsftRetention<RecordingRetentionPass>((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void NameFor_IsTheFullTypeName()
    {
        BsftRetentionOptions.NameFor<RecordingRetentionPass>()
            .Should().Be(typeof(RecordingRetentionPass).FullName);
    }

    /// <summary>A second pass, so the named-options separation can be asserted against two of them.</summary>
    private sealed class SecondRetentionPass : IRetentionPass
    {
        public Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
