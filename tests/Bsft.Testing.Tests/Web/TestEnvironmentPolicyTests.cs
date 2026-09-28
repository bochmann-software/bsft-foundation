using Bsft.Testing.Web;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Bsft.Testing.Tests.Web;

public class TestEnvironmentPolicyTests
{
    /// <summary>
    /// A deployed host is not recognised by the two well-known names alone — plenty of them call
    /// themselves Live or PRD — so anything the policy does not allow is not a test host.
    /// </summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Live")]
    [InlineData("PRD")]
    [InlineData("UAT")]
    [InlineData("Development")]
    [InlineData("")]
    public void Unrecognised_environment_is_not_a_test_host(string environmentName)
    {
        CreatePolicy(environmentName).IsTestHost.Should().BeFalse();
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("testing")]
    public void Testing_environment_is_a_test_host(string environmentName)
    {
        CreatePolicy(environmentName).IsTestHost.Should().BeTrue();
    }

    [Fact]
    public void Environment_named_at_construction_is_a_test_host()
    {
        CreatePolicy("IntegrationTest", "IntegrationTest").IsTestHost.Should().BeTrue();
    }

    [Fact]
    public void Environment_policy_reports_what_it_accepts()
    {
        var policy = CreatePolicy("Production", "IntegrationTest");

        policy.IsTestHost.Should().BeFalse();
        policy.EnvironmentName.Should().Be("Production");
        policy.AllowedEnvironments.Should().BeEquivalentTo(["Testing", "IntegrationTest"]);
    }

    [Fact]
    public void Environment_policy_ignores_blank_additional_names()
    {
        var policy = CreatePolicy("Testing", "", "   ");

        policy.AllowedEnvironments.Should().BeEquivalentTo(["Testing"]);
    }

    [Fact]
    public void Environment_policy_rejects_null_arguments()
    {
        var withoutEnvironment = () => new TestEnvironmentPolicy(null!);
        var withoutNames = () => new TestEnvironmentPolicy(CreateEnvironment("Testing"), null!);

        withoutEnvironment.Should().Throw<ArgumentNullException>().WithParameterName("environment");
        withoutNames.Should().Throw<ArgumentNullException>().WithParameterName("additionalEnvironments");
    }

    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return environment;
    }

    private static TestEnvironmentPolicy CreatePolicy(string environmentName, params string[] additional) =>
        new(CreateEnvironment(environmentName), additional);
}
