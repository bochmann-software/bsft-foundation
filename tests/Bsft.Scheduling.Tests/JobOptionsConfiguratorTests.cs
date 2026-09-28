using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Bsft.Scheduling.Tests;

public sealed class JobOptionsConfiguratorTests
{
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bsft:Scheduling:Jobs:nightly:Enabled"] = "false",
            ["Bsft:Scheduling:Jobs:nightly:HeartbeatTolerance"] = "02:00:00",
        })
        .Build();

    [Fact]
    public void ThePerJobSectionIsBoundOntoThatJobsOptions()
    {
        var options = new BsftJobOptions();

        new JobOptionsConfigurator(_configuration).Configure("nightly", options);

        options.Enabled.Should().BeFalse();
        options.HeartbeatTolerance.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void AJobWithNoSectionKeepsItsDefaults()
    {
        var options = new BsftJobOptions();

        new JobOptionsConfigurator(_configuration).Configure("hourly", options);

        options.Enabled.Should().BeTrue();
        options.HeartbeatTolerance.Should().BeNull();
    }

    [Fact]
    public void TheUnnamedInstanceBindsNothing()
    {
        var options = new BsftJobOptions();

        // Binding the parent section onto it would hand every job's defaults the first job's values.
        new JobOptionsConfigurator(_configuration).Configure(options);

        options.Enabled.Should().BeTrue();
        options.HeartbeatTolerance.Should().BeNull();
    }

    [Fact]
    public void TheNullGuardsRejectMissingArguments()
    {
        ((Action)(() => new JobOptionsConfigurator(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");

        ((Action)(() => new JobOptionsConfigurator(_configuration).Configure("nightly", null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
