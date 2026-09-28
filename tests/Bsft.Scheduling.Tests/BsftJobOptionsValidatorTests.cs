using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

public sealed class BsftJobOptionsValidatorTests
{
    private readonly BsftJobOptionsValidator _validator = new();

    [Fact]
    public void TheDefaultsAreValidAndSwitchTheJobOn()
    {
        var options = new BsftJobOptions();

        options.Enabled.Should().BeTrue();
        options.IsHeartbeatMonitored.Should().BeTrue();
        options.HeartbeatTolerance.Should().BeNull();
        options.HeartbeatToleranceFactor.Should().Be(2);
        options.HeartbeatGrace.Should().Be(TimeSpan.FromMinutes(1));
        BsftJobOptions.SectionName.Should().Be("Bsft:Scheduling:Jobs");

        _validator.Validate("nightly", options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ANonPositiveExplicitToleranceIsRejectedAndNamesTheJob()
    {
        Failures("nightly", options => options.HeartbeatTolerance = TimeSpan.Zero)
            .Should().ContainSingle().Which.Should().Contain("'nightly'");
    }

    [Fact]
    public void AToleranceFactorBelowOneIsRejected()
    {
        Failures("nightly", options => options.HeartbeatToleranceFactor = 0.5)
            .Should().ContainSingle().Which.Should().Contain("HeartbeatToleranceFactor");
    }

    [Fact]
    public void ANegativeGraceIsRejected()
    {
        Failures("nightly", options => options.HeartbeatGrace = TimeSpan.FromSeconds(-1))
            .Should().ContainSingle().Which.Should().Contain("HeartbeatGrace");
    }

    [Fact]
    public void TheUnnamedInstanceIsNamedAsSuchRatherThanLeftBlank()
    {
        Failures(name: null, options => options.HeartbeatGrace = TimeSpan.FromSeconds(-1))
            .Should().ContainSingle().Which.Should().Contain("(unnamed)");
    }

    [Fact]
    public void EveryComplaintIsCollectedRatherThanTheFirstOne()
    {
        Failures("nightly", options =>
        {
            options.HeartbeatTolerance = TimeSpan.FromMinutes(-1);
            options.HeartbeatToleranceFactor = 0;
            options.HeartbeatGrace = TimeSpan.FromSeconds(-1);
        }).Should().HaveCount(3);
    }

    [Fact]
    public void MissingOptionsAreRejected()
    {
        ((Action)(() => _validator.Validate("nightly", null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AToleranceFactorThatIsNotAFiniteNumberIsRejected(double factor)
    {
        Failures("nightly", options => options.HeartbeatToleranceFactor = factor)
            .Should().ContainSingle().Which.Should().Contain("HeartbeatToleranceFactor");
    }

    private IEnumerable<string> Failures(string? name, Action<BsftJobOptions> configure)
    {
        var options = new BsftJobOptions();
        configure(options);

        var result = _validator.Validate(name, options);
        result.Failed.Should().BeTrue();

        return result.Failures ?? [];
    }
}
