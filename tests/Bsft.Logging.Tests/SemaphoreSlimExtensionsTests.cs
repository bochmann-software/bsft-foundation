using FluentAssertions;
using Xunit;

namespace Bsft.Logging.Tests;

public class SemaphoreSlimExtensionsTests
{
    [Fact]
    public void Hold_WhenDisposed_ReleasesTheSemaphore()
    {
        using var gate = new SemaphoreSlim(1, 1);

        var hold = gate.Hold();
        gate.CurrentCount.Should().Be(0, "the hold owns the only slot");

        hold.Dispose();

        gate.CurrentCount.Should().Be(1);
    }

    [Fact]
    public void Hold_WithoutASemaphore_Throws()
    {
        var act = () => SemaphoreSlimExtensions.Hold(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HoldWithin_WhenTheSemaphoreIsFree_ReturnsAHoldThatReleases()
    {
        using var gate = new SemaphoreSlim(1, 1);

        var hold = gate.HoldWithin();

        hold.Should().NotBeNull();
        gate.CurrentCount.Should().Be(0);
        hold!.Dispose();
        gate.CurrentCount.Should().Be(1);
    }

    [Fact]
    public void HoldWithin_WhenTheBudgetIsAlreadySpent_ReturnsNull()
    {
        using var gate = new SemaphoreSlim(0, 1);
        using var spent = new CancellationTokenSource();
        spent.Cancel();

        gate.HoldWithin(spent.Token).Should().BeNull("a spent budget gives up instead of waiting");
    }

    [Fact]
    public void HoldWithin_WithoutASemaphore_Throws()
    {
        var act = () => SemaphoreSlimExtensions.HoldWithin(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
