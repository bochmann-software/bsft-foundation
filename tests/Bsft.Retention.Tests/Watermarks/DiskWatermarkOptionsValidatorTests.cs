using Bsft.Retention.Watermarks;
using FluentAssertions;
using Xunit;

namespace Bsft.Retention.Tests.Watermarks;

public sealed class DiskWatermarkOptionsValidatorTests
{
    private readonly DiskWatermarkOptionsValidator _validator = new();

    [Fact]
    public void Validate_Defaults_Succeed()
    {
        _validator.Validate("pass", new DiskWatermarkOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_BothLimitsWithAPath_Succeed()
    {
        var options = new DiskWatermarkOptions
        {
            MaxArchiveBytes = 1_000,
            MinAvailableBytes = 500,
            VolumePath = "/var/archive",
            TargetFillRatio = 1,
        };

        _validator.Validate("pass", options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EveryRuleBroken_ReportsEveryFailureWithThePassName()
    {
        var options = new DiskWatermarkOptions
        {
            MaxArchiveBytes = 0,
            MinAvailableBytes = -1,
            VolumePath = " ",
            TargetFillRatio = 0,
        };

        var result = _validator.Validate("pass", options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(4).And.OnlyContain(failure => failure.StartsWith("pass: ", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains(nameof(DiskWatermarkOptions.MaxArchiveBytes)));
        result.Failures.Should().Contain(failure => failure.Contains(nameof(DiskWatermarkOptions.VolumePath)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public void Validate_RatioOutsideTheRange_Fails(double ratio)
    {
        var result = _validator.Validate("pass", new DiskWatermarkOptions { TargetFillRatio = ratio });

        result.Failures.Should().ContainSingle().Which.Should().Contain(nameof(DiskWatermarkOptions.TargetFillRatio));
    }

    [Fact]
    public void Validate_UnnamedOptions_NameTheOptionsType()
    {
        var result = _validator.Validate(null, new DiskWatermarkOptions { MaxArchiveBytes = -5 });

        result.Failures.Should().ContainSingle().Which.Should().StartWith($"{nameof(DiskWatermarkOptions)}: ");
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        var act = () => _validator.Validate("pass", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
