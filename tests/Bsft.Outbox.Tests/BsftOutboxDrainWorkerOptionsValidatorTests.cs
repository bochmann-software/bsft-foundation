using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Outbox.Tests;

/// <summary>Tests what the host is told at start about a shutdown grace no timer can count down.</summary>
public sealed class BsftOutboxDrainWorkerOptionsValidatorTests
{
    private static readonly BsftOutboxDrainWorkerOptionsValidator Validator = new(Options.Create(new HostOptions()));

    [Fact]
    public void Validate_WithTheDefaults_Succeeds()
    {
        Validator.Validate("primary", new BsftOutboxDrainWorkerOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithAZeroGrace_Succeeds()
    {
        Validator.Validate("primary", new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.Zero })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithTheLongestGraceATimerTakes_Succeeds()
    {
        ValidatorWith(Timeout.InfiniteTimeSpan).Validate(
                "primary", new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.FromMilliseconds(int.MaxValue) })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithANegativeGrace_FailsNamingTheOutboxAndTheOption()
    {
        var result = Validator.Validate(
            "primary", new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.FromSeconds(-1) });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("primary").And.Contain(nameof(BsftOutboxDrainWorkerOptions.ShutdownGrace));
    }

    [Fact]
    public void Validate_WithAGraceBeyondATimer_Fails()
    {
        var result = Validator.Validate(
            null, new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.FromMilliseconds(int.MaxValue + 1L) });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().NotContain("named");
    }

    [Fact]
    public void Validate_WithoutOptions_ThrowsArgumentNullException()
    {
        var act = () => Validator.Validate("primary", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Validate_WithAGraceBelowTheHostShutdownTimeout_Succeeds()
    {
        var validator = ValidatorWith(TimeSpan.FromSeconds(10));

        validator.Validate("primary", new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.FromSeconds(9) })
            .Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void Validate_WithAGraceAtOrBeyondTheHostShutdownTimeout_FailsNamingBoth(int graceSeconds)
    {
        var validator = ValidatorWith(TimeSpan.FromSeconds(10));

        var result = validator.Validate(
            "primary", new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.FromSeconds(graceSeconds) });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("primary")
            .And.Contain(nameof(HostOptions.ShutdownTimeout))
            .And.Contain(nameof(BsftOutboxDrainWorkerOptions.ShutdownGrace));
    }

    [Fact]
    public void Validate_WithoutAName_AndAGraceBeyondTheHostTimeout_FailsWithoutNamingAnOutbox()
    {
        var result = ValidatorWith(TimeSpan.FromSeconds(1))
            .Validate(null, new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.FromSeconds(5) });

        result.FailureMessage.Should().NotContain("named");
    }

    /// <summary>An unbounded host stop cannot be outrun, and a zero timeout is the host's own choice not to wait.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Validate_WhenTheHostDoesNotBoundTheStop_Succeeds(int timeoutMilliseconds)
    {
        var validator = ValidatorWith(TimeSpan.FromMilliseconds(timeoutMilliseconds));

        validator.Validate("primary", new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.FromMinutes(5) })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithoutHostOptions_ThrowsArgumentNullException()
    {
        var act = () => new BsftOutboxDrainWorkerOptionsValidator(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("hostOptions");
    }

    private static BsftOutboxDrainWorkerOptionsValidator ValidatorWith(TimeSpan shutdownTimeout) =>
        new(Options.Create(new HostOptions { ShutdownTimeout = shutdownTimeout }));
}
