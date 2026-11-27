using Bsft.Retention.Testing;
using FluentAssertions;
using Xunit;

namespace Bsft.Retention.Tests.Testing;

public sealed class FakeVolumeSpaceProviderTests
{
    [Fact]
    public void GetAvailableBytes_ReportsTheSetSpaceAndRecordsThePaths()
    {
        var fake = new FakeVolumeSpaceProvider { AvailableBytes = 42 };

        fake.GetAvailableBytes("/a").Should().Be(42);
        fake.AvailableBytes = 7;
        fake.GetAvailableBytes("/b").Should().Be(7);

        fake.MeasuredPaths.Should().Equal("/a", "/b");
    }

    [Fact]
    public void GetAvailableBytes_NullPath_Throws()
    {
        var act = () => new FakeVolumeSpaceProvider().GetAvailableBytes(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
