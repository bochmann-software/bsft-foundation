using Bsft.Core.Hosting;
using Bsft.Hosting.StartGates.DependencyInjection;
using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.HealthChecks;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bsft.Hosting.Tests.HealthChecks;

public sealed class HostedWorkStartGateHealthCheckTests
{
    [Fact]
    public void Constructor_NullGates_Throws() =>
        ((Action)(() => new HostedWorkStartGateHealthCheck(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("gates");

    [Fact]
    public async Task CheckHealthAsync_WithNoGates_IsHealthy()
    {
        var result = await new HostedWorkStartGateHealthCheck([]).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WithAGateThatIsNotOverdue_IsHealthy()
    {
        var gate = new ControllableStartGate { Overdue = false };

        var result = await new HostedWorkStartGateHealthCheck([gate]).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WithAnOverdueGate_IsUnhealthyAndNamesIt()
    {
        var gate = new ControllableStartGate { Overdue = true };

        var result = await new HostedWorkStartGateHealthCheck([new ControllableStartGate(), gate])
            .CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain(nameof(ControllableStartGate));
    }

    [Fact]
    public async Task CheckHealthAsync_WithAGateThatCannotSayItIsOverdue_IsHealthy()
    {
        var gate = Substitute.For<IHostedWorkStartGate>();

        var result = await new HostedWorkStartGateHealthCheck([gate]).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void AddBsftHealthChecks_ByDefault_RegistersNoStartGateCheck()
    {
        using var provider = BuildProvider(configure: null);

        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Should().NotContain(registration => registration.Name == "hosted-work-start-gates");
    }

    [Fact]
    public async Task AddBsftHealthChecks_WithTheStartGateCheck_ReportsTheRegisteredGateWhenItIsOverdue()
    {
        var gate = new ControllableStartGate { Overdue = true };
        using var provider = BuildProvider(configuration => configuration.AddHostedWorkStartGates(), gate);

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries["hosted-work-start-gates"].Status.Should().Be(HealthStatus.Unhealthy);
        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(registration => registration.Name == "hosted-work-start-gates").Tags.Should().Contain("ready");

        gate.Overdue = false;
        (await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync())
            .Entries["hosted-work-start-gates"].Status.Should().Be(HealthStatus.Healthy);
    }

    private static ServiceProvider BuildProvider(Action<BsftHealthCheckConfiguration>? configure, ControllableStartGate? gate = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (gate is not null)
            services.AddBsftHostedWorkStartGate(_ => gate);

        services.AddBsftHealthChecks(configure);

        return services.BuildServiceProvider();
    }
}
