using Bsft.Retention.Watermarks;
using FluentAssertions;
using Xunit;

namespace Bsft.Retention.Tests.Watermarks;

public sealed class VolumeSpaceProviderTests
{
    [Fact]
    public void GetAvailableBytes_ThisMachinesTempDirectory_MatchesDriveInfo()
    {
        var path = Path.GetTempPath();

        var available = new VolumeSpaceProvider().GetAvailableBytes(path);

        // The volume is shared with everything else on the machine, so the two readings only agree in
        // magnitude; what matters is that the right volume was asked.
        var expected = new DriveInfo(path).AvailableFreeSpace;
        available.Should().BePositive();
        ((double)available).Should().BeApproximately(expected, expected * 0.05);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GetAvailableBytes_NoPath_Throws(string? path)
    {
        var act = () => new VolumeSpaceProvider().GetAvailableBytes(path!);

        act.Should().Throw<ArgumentException>().WithParameterName("path");
    }
}
