using Bsft.Hosting.BackgroundWork;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests;

public sealed class BsftRunOnceOptionsValidatorTests
{
    private readonly BsftRunOnceOptionsValidator _validation = new();

    [Fact]
    public void TheDefaultsAreValid()
    {
        _validation.Validate(name: null, new BsftRunOnceOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ANegativeStartupDelayIsRejected()
    {
        Failures(options => options.StartupDelay = TimeSpan.FromSeconds(-1))
            .Should().ContainSingle().Which.Should().Contain("StartupDelay");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonPositiveInitialRetryDelayIsRejected(int seconds)
    {
        Failures(options => options.InitialRetryDelay = TimeSpan.FromSeconds(seconds))
            .Should().Contain(failure => failure.Contains("InitialRetryDelay", StringComparison.Ordinal));
    }

    [Fact]
    public void ACapBelowTheFirstDelayIsRejected()
    {
        Failures(options =>
        {
            options.InitialRetryDelay = TimeSpan.FromMinutes(2);
            options.MaxRetryDelay = TimeSpan.FromMinutes(1);
        }).Should().ContainSingle().Which.Should().Contain("MaxRetryDelay");
    }

    [Fact]
    public void AShrinkingBackoffIsRejected()
    {
        Failures(options => options.BackoffFactor = 0.5)
            .Should().ContainSingle().Which.Should().Contain("BackoffFactor");
    }

    [Fact]
    public void ANegativeAttemptBudgetIsRejected()
    {
        Failures(options => options.MaxAttempts = -1)
            .Should().ContainSingle().Which.Should().Contain("MaxAttempts");
    }

    [Fact]
    public void EveryComplaintIsCollectedRatherThanTheFirstOne()
    {
        Failures(options =>
        {
            options.StartupDelay = TimeSpan.FromSeconds(-1);
            options.BackoffFactor = 0;
            options.MaxAttempts = -1;
        }).Should().HaveCount(3);
    }

    [Fact]
    public void MissingOptionsAreRejected()
    {
        ((Action)(() => _validation.Validate(name: null, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ABackoffFactorThatIsNotAFiniteNumberIsRejected(double factor)
    {
        // NaN would pass the "at least 1" test — every comparison against it is false — and then turn
        // the first backoff into no delay at all.
        Failures(options => options.BackoffFactor = factor)
            .Should().ContainSingle().Which.Should().Contain("BackoffFactor");
    }

    [Fact]
    public void ADelayLongerThanTaskDelayAcceptsIsRejectedAtStartup()
    {
        // Task.Delay throws beyond roughly 49.7 days, and it would do so from inside the loop rather
        // than while the host is still coming up.
        Failures(options => options.StartupDelay = TimeSpan.FromDays(60))
            .Should().ContainSingle().Which.Should().Contain("StartupDelay");

        Failures(options =>
        {
            options.InitialRetryDelay = TimeSpan.FromDays(60);
            options.MaxRetryDelay = TimeSpan.FromDays(90);
        }).Should().HaveCount(2);
    }

    private IEnumerable<string> Failures(Action<BsftRunOnceOptions> configure)
    {
        var options = new BsftRunOnceOptions();
        configure(options);

        var result = _validation.Validate(name: null, options);
        result.Failed.Should().BeTrue();

        return result.Failures ?? [];
    }
}
