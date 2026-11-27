using Bsft.Retention.Watermarks;
using FluentAssertions;
using Xunit;

namespace Bsft.Retention.Tests.Watermarks;

public sealed class DiskWatermarkStatusStoreTests
{
    private static readonly DiskWatermarkStatusInfo Snapshot =
        new(DiskWatermarkState.Draining, 10, 20, 5, new DateTimeOffset(2026, 11, 12, 6, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Find_NothingSaved_ReturnsNull()
    {
        new DiskWatermarkStatusStore().Find("pass").Should().BeNull();
    }

    [Fact]
    public void Save_ThenFind_ReturnsTheLatestSnapshotPerPass()
    {
        var store = new DiskWatermarkStatusStore();
        var later = Snapshot with { State = DiskWatermarkState.Within };

        store.Save("pass", Snapshot);
        store.Save("pass", later);
        store.Save("other", Snapshot);

        store.Find("pass").Should().Be(later);
        store.Find("other").Should().Be(Snapshot);
    }

    [Theory]
    [InlineData(DiskWatermarkState.Draining, true)]
    [InlineData(DiskWatermarkState.Unreachable, true)]
    [InlineData(DiskWatermarkState.Within, false)]
    public void IsDraining_AfterASnapshot_FollowsItsState(DiskWatermarkState state, bool expected)
    {
        var store = new DiskWatermarkStatusStore();

        store.Save("pass", Snapshot with { State = state });

        store.IsDraining("pass").Should().Be(expected);
        store.IsDraining("other").Should().BeFalse();
    }

    [Fact]
    public void IsDraining_AfterAFailedMeasurement_KeepsTheDrainItInterrupted()
    {
        var store = new DiskWatermarkStatusStore();
        var failed = Snapshot with { State = DiskWatermarkState.MeasurementFailed };

        store.Save("pass", Snapshot);
        store.Save("pass", failed);
        store.Save("fresh", failed);

        store.IsDraining("pass").Should().BeTrue();
        store.IsDraining("fresh").Should().BeFalse();
        store.Find("pass").Should().Be(failed);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var store = new DiskWatermarkStatusStore();

        var calls = new Action[]
        {
            () => store.Find(null!),
            () => store.IsDraining(null!),
            () => store.Save(null!, Snapshot),
            () => store.Save("pass", null!),
        };

        calls.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
    }
}
