using FluentAssertions;
using Xunit;

namespace Bsft.Instrumentation.Tests;

public sealed class ObservabilitySourceRegistrationTests
{
    [Fact]
    public void Defaults_TracingAndMetrics_AreEnabled()
    {
        var registration = new ObservabilitySourceRegistration("Bsft.Mediator");

        registration.Name.Should().Be("Bsft.Mediator");
        registration.EnableTracing.Should().BeTrue();
        registration.EnableMetrics.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullOrEmptyName_Throws()
    {
        var act = () => new ObservabilitySourceRegistration("  ");

        act.Should().Throw<ArgumentException>().WithParameterName("Name");
    }

    [Fact]
    public void ExplicitFlags_AreHonored()
    {
        var registration = new ObservabilitySourceRegistration("Bsft.Saga", EnableTracing: false, EnableMetrics: true);

        registration.EnableTracing.Should().BeFalse();
        registration.EnableMetrics.Should().BeTrue();
    }
}
