using Bsft.Retention.Watermarks;
using FluentAssertions;
using Xunit;

namespace Bsft.Retention.Tests.Watermarks;

public sealed class DiskWatermarkPlannerTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly DiskWatermarkPlanner _planner = new();

    private static DiskWatermarkItemInfo[] Items(int count, long bytes)
        => Enumerable.Range(0, count).Select(index => new DiskWatermarkItemInfo($"item-{index}", bytes, Created.AddMinutes(index))).ToArray();

    [Fact]
    public void Plan_NoLimitConfigured_RequiresNothing()
    {
        var result = _planner.Plan(new DiskWatermarkPlanRequest(1_000, null, Items(10, 100), false), new DiskWatermarkOptions());

        result.Should().BeEquivalentTo(new DiskWatermarkPlanResult([], 0, 0, DiskWatermarkState.Within));
    }

    [Fact]
    public void Plan_ArchiveNotListed_LeavesTheArchiveLimitOutOfTheShortfall()
    {
        var options = new DiskWatermarkOptions { MaxArchiveBytes = 100, MinAvailableBytes = 1_000, VolumePath = "/", TargetFillRatio = 0.5 };

        var result = _planner.Plan(new DiskWatermarkPlanRequest(null, 1_500, [], true), options);

        result.RequiredBytes.Should().Be(500, "only the volume floor can be held to its target without a listing");
        result.State.Should().Be(DiskWatermarkState.Unreachable);
    }

    [Fact]
    public void Plan_ArchiveAtItsLimit_IsNotACrossing()
    {
        var options = new DiskWatermarkOptions { MaxArchiveBytes = 1_000 };

        var result = _planner.Plan(new DiskWatermarkPlanRequest(1_000, null, Items(10, 100), false), options);

        result.State.Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public void Plan_ArchiveOverItsLimit_ChoosesTheOldestUntilTheTargetHolds()
    {
        var options = new DiskWatermarkOptions { MaxArchiveBytes = 900, TargetFillRatio = 0.5 };

        var result = _planner.Plan(new DiskWatermarkPlanRequest(1_000, null, Items(10, 100), false), options);

        result.RequiredBytes.Should().Be(550);
        result.PlannedBytes.Should().Be(600);
        result.Evictions.Select(item => item.Key).Should().Equal("item-0", "item-1", "item-2", "item-3", "item-4", "item-5");
        result.State.Should().Be(DiskWatermarkState.Draining);
    }

    [Fact]
    public void Plan_VolumeBelowItsFloor_FreesUpToTheFloorDividedByTheRatio()
    {
        var options = new DiskWatermarkOptions { MinAvailableBytes = 1_000, VolumePath = "/", TargetFillRatio = 0.5 };

        var result = _planner.Plan(new DiskWatermarkPlanRequest(0, 999, Items(20, 100), false), options);

        result.RequiredBytes.Should().Be(1_001);
        result.Evictions.Should().HaveCount(11);
    }

    [Fact]
    public void Plan_BothCrossed_FreesWhatTheLargerShortfallNeeds()
    {
        var options = new DiskWatermarkOptions { MaxArchiveBytes = 1_000, MinAvailableBytes = 1_000, VolumePath = "/", TargetFillRatio = 1 };

        var result = _planner.Plan(new DiskWatermarkPlanRequest(1_100, 500, Items(11, 100), false), options);

        result.RequiredBytes.Should().Be(500);
    }

    [Fact]
    public void Plan_DrainingBetweenTargetAndLimit_KeepsDraining()
    {
        var options = new DiskWatermarkOptions { MaxArchiveBytes = 1_000, TargetFillRatio = 0.8 };

        var notDraining = _planner.Plan(new DiskWatermarkPlanRequest(900, null, Items(9, 100), false), options);
        var draining = _planner.Plan(new DiskWatermarkPlanRequest(900, null, Items(9, 100), true), options);

        notDraining.State.Should().Be(DiskWatermarkState.Within);
        draining.RequiredBytes.Should().Be(100);
        draining.State.Should().Be(DiskWatermarkState.Draining);
    }

    [Fact]
    public void Plan_NotEnoughOldEnough_IsUnreachableAndChoosesEverything()
    {
        var options = new DiskWatermarkOptions { MaxArchiveBytes = 100 };

        var result = _planner.Plan(new DiskWatermarkPlanRequest(1_000, null, Items(3, 100), false), options);

        result.State.Should().Be(DiskWatermarkState.Unreachable);
        result.Evictions.Should().HaveCount(3);
        result.PlannedBytes.Should().Be(300);
    }

    [Fact]
    public void Plan_TargetPastTheRange_Saturates()
    {
        var options = new DiskWatermarkOptions { MinAvailableBytes = long.MaxValue, VolumePath = "/", TargetFillRatio = 0.5 };

        var result = _planner.Plan(new DiskWatermarkPlanRequest(0, 0, [], false), options);

        result.RequiredBytes.Should().Be(long.MaxValue);
        result.State.Should().Be(DiskWatermarkState.Unreachable);
    }

    [Fact]
    public void Plan_FloorWithoutAMeasurement_IgnoresTheFloor()
    {
        var options = new DiskWatermarkOptions { MinAvailableBytes = 1_000 };

        var result = _planner.Plan(new DiskWatermarkPlanRequest(0, null, Items(1, 100), true), options);

        result.RequiredBytes.Should().Be(0);
    }

    [Fact]
    public void Plan_NullArgument_Throws()
    {
        var calls = new Action[]
        {
            () => _planner.Plan(null!, new DiskWatermarkOptions()),
            () => _planner.Plan(new DiskWatermarkPlanRequest(0, null, [], false), null!),
        };

        calls.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
    }
}
