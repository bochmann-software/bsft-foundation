using Bsft.Retention.Testing;
using Bsft.Retention.Tests.Fakes;
using Bsft.Retention.Watermarks;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Retention.Tests.Watermarks;

public sealed class DiskWatermarkRetentionPassTests
{
    private const string VolumePath = "/var/archive";

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = Now.AddHours(-1);
    private static readonly string PassName = DiskWatermarkOptions.NameFor<FakeDiskWatermarkSource>();

    private sealed class Harness
    {
        public FakeDiskWatermarkSource Source { get; } = new();

        public FakeVolumeSpaceProvider Volume { get; } = new() { AvailableBytes = long.MaxValue / 2 };

        public DiskWatermarkStatusStore Status { get; } = new();

        public RecordingLogger<DiskWatermarkRetentionPass<FakeDiskWatermarkSource>> Logger { get; } = new();

        public DiskWatermarkOptions Options { get; } = new();

        public DiskWatermarkStatusInfo? Snapshot => Status.Find(PassName);

        /// <summary>Fills the archive with items of the given sizes, oldest first, all older than the cutoff.</summary>
        public Harness WithItems(params long[] sizes)
        {
            for (var index = 0; index < sizes.Length; index++)
                Source.Add($"item-{index}", sizes[index], Cutoff.AddDays(-sizes.Length + index));

            return this;
        }

        public DiskWatermarkRetentionPass<FakeDiskWatermarkSource> CreatePass()
        {
            var monitor = Substitute.For<IOptionsMonitor<DiskWatermarkOptions>>();
            monitor.Get(PassName).Returns(Options);

            return new DiskWatermarkRetentionPass<FakeDiskWatermarkSource>(
                Source, Volume, new DiskWatermarkPlanner(), monitor, Status, new RecordingTimeProvider(Now), Logger);
        }

        /// <summary>Runs one sweep the way the sweeper does: batches against one cutoff until one comes back short.</summary>
        public async Task<int> SweepAsync(int batchSize = 100)
        {
            var pass = CreatePass();
            var total = 0;

            while (true)
            {
                var purged = await pass.PurgeBatchAsync(batchSize, Cutoff);
                total += purged;

                if (purged < batchSize)
                    return total;
            }
        }
    }

    [Fact]
    public async Task PurgeBatchAsync_BothLimitsHold_DeletesNothingAndRecordsWithin()
    {
        var harness = new Harness().WithItems(100, 100, 100);
        harness.Options.MaxArchiveBytes = 1_000;
        harness.Options.MinAvailableBytes = 500;
        harness.Options.VolumePath = VolumePath;
        harness.Volume.AvailableBytes = 10_000;

        var purged = await harness.CreatePass().PurgeBatchAsync(10, Cutoff);

        purged.Should().Be(0);
        harness.Source.Deleted.Should().BeEmpty();
        harness.Volume.MeasuredPaths.Should().Equal(VolumePath);
        harness.Snapshot.Should().Be(new DiskWatermarkStatusInfo(DiskWatermarkState.Within, 300, 10_000, 0, Now));
        harness.Logger.Levels.Should().Equal(LogLevel.Debug);
    }

    [Fact]
    public async Task PurgeBatchAsync_ArchiveOverItsLimit_DeletesTheOldestDownToTheTarget()
    {
        // 1000 bytes against a limit of 900: the target is 810, so 190 bytes have to go — two items.
        var harness = new Harness().WithItems(100, 100, 100, 100, 100, 100, 100, 100, 100, 100);
        harness.Options.MaxArchiveBytes = 900;

        var purged = await harness.CreatePass().PurgeBatchAsync(10, Cutoff);

        purged.Should().Be(2);
        harness.Source.Deleted.Select(item => item.Key).Should().Equal("item-0", "item-1");
        harness.Volume.MeasuredPaths.Should().BeEmpty("no floor is configured, so no volume is measured");
        harness.Snapshot.Should().Be(new DiskWatermarkStatusInfo(DiskWatermarkState.Within, 1_000, null, 190, Now));
        harness.Logger.Levels.Should().Equal(LogLevel.Information, LogLevel.Information);
    }

    [Fact]
    public async Task PurgeBatchAsync_VolumeBelowItsFloor_DeletesUntilTheFloorHoldsWithRoom()
    {
        // 900 bytes available against a floor of 1000 at a ratio of 0.5: the target is 2000, so 1100
        // bytes go, which takes six items of 200.
        var harness = new Harness().WithItems(200, 200, 200, 200, 200, 200, 200);
        harness.Options.MinAvailableBytes = 1_000;
        harness.Options.VolumePath = VolumePath;
        harness.Options.TargetFillRatio = 0.5;
        harness.Volume.AvailableBytes = 900;

        var purged = await harness.SweepAsync();

        purged.Should().Be(6);
        harness.Source.Items.Select(item => item.Key).Should().Equal("item-6");
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Within);
        harness.Snapshot.RequiredBytes.Should().Be(1_100);
    }

    [Fact]
    public async Task PurgeBatchAsync_OnlyAFloorThatHolds_DoesNotListTheArchive()
    {
        var harness = new Harness().WithItems(200, 200);
        harness.Options.MinAvailableBytes = 1_000;
        harness.Options.VolumePath = VolumePath;
        harness.Volume.AvailableBytes = 1_000;

        var purged = await harness.SweepAsync();

        purged.Should().Be(0);
        harness.Source.Listings.Should().Be(0);
        harness.Snapshot.Should().Be(new DiskWatermarkStatusInfo(DiskWatermarkState.Within, null, 1_000, 0, Now));
    }

    [Fact]
    public async Task PurgeBatchAsync_OnlyAFloorThatHoldsDuringADrain_ListsAndDrainsToTheTarget()
    {
        // The floor of 1000 holds, but a drain is under way, so the target of 2000 decides.
        var harness = new Harness().WithItems(200, 200, 200, 200, 200, 200);
        harness.Options.MinAvailableBytes = 1_000;
        harness.Options.VolumePath = VolumePath;
        harness.Options.TargetFillRatio = 0.5;
        harness.Volume.AvailableBytes = 1_500;
        harness.Status.Save(PassName, new DiskWatermarkStatusInfo(DiskWatermarkState.Draining, 1_200, 900, 1_100, Now));

        var purged = await harness.SweepAsync();

        purged.Should().Be(3);
        harness.Source.Listings.Should().Be(1);
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public async Task PurgeBatchAsync_NoLimitConfigured_DoesNotListTheArchive()
    {
        var harness = new Harness().WithItems(100);

        var purged = await harness.SweepAsync();

        purged.Should().Be(0);
        harness.Source.Listings.Should().Be(0);
        harness.Volume.MeasuredPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task PurgeBatchAsync_PlanLargerThanABatch_HonoursTheBatchSizeAndListsOncePerSweep()
    {
        var harness = new Harness().WithItems(100, 100, 100, 100, 100);
        harness.Options.MaxArchiveBytes = 300;
        harness.Options.TargetFillRatio = 1;
        var pass = harness.CreatePass();

        var first = await pass.PurgeBatchAsync(1, Cutoff);
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Draining);
        var second = await pass.PurgeBatchAsync(1, Cutoff);
        var third = await pass.PurgeBatchAsync(1, Cutoff);

        (first, second, third).Should().Be((1, 1, 0));
        harness.Source.Listings.Should().Be(1);
        harness.Snapshot.State.Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public async Task PurgeBatchAsync_DrainLeftUnfinished_ContinuesToTheTargetInTheNextSweep()
    {
        // The first sweep stops after one batch, as the sweeper's bound would make it, with the
        // archive back under its limit but not yet at its target.
        var harness = new Harness().WithItems(100, 100, 100, 100, 100, 100, 100, 100, 100, 100);
        harness.Options.MaxArchiveBytes = 950;
        harness.Options.TargetFillRatio = 0.8;

        await harness.CreatePass().PurgeBatchAsync(1, Cutoff);
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Draining);

        var purged = await harness.SweepAsync();

        purged.Should().Be(2, "900 bytes are under the limit of 950 but over the target of 760");
        harness.Source.Items.Sum(item => item.Bytes).Should().Be(700);
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public async Task PurgeBatchAsync_DrainInterruptedByAFailedMeasurement_StillReachesTheTarget()
    {
        var harness = new Harness().WithItems(100, 100, 100, 100, 100, 100, 100, 100, 100, 100);
        harness.Options.MaxArchiveBytes = 950;
        harness.Options.TargetFillRatio = 0.8;

        await harness.CreatePass().PurgeBatchAsync(1, Cutoff);
        harness.Source.ListFailure = new IOException("archive offline");
        var failed = () => harness.SweepAsync();
        await failed.Should().ThrowAsync<IOException>();
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.MeasurementFailed);
        harness.Source.ListFailure = null;

        var purged = await harness.SweepAsync();

        purged.Should().Be(2, "the failed measurement does not end the drain the first sweep started");
        harness.Source.Items.Sum(item => item.Bytes).Should().Be(700);
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public async Task PurgeBatchAsync_CancelledWhileMeasuring_RecordsNoFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = new Harness().WithItems(100, 100);
        harness.Options.MaxArchiveBytes = 100;
        await cancellation.CancelAsync();

        var act = () => harness.CreatePass().PurgeBatchAsync(10, Cutoff, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Snapshot.Should().BeNull("a host stopping mid-measurement says nothing about the archive");
        harness.Source.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task PurgeBatchAsync_UnderTheLimitWithoutADrain_LeavesTheArchiveAboveItsTarget()
    {
        // Hysteresis works the other way round as well: between the target and the limit, nothing is
        // deleted until the limit is crossed.
        var harness = new Harness().WithItems(100, 100, 100, 100, 100, 100, 100, 100, 100);
        harness.Options.MaxArchiveBytes = 950;
        harness.Options.TargetFillRatio = 0.8;

        var purged = await harness.SweepAsync();

        purged.Should().Be(0);
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public async Task PurgeBatchAsync_ItemsYoungerThanTheCutoff_CountButAreNeverDeleted()
    {
        var harness = new Harness().WithItems(100, 100);
        harness.Source.Add("young", 1_000, Cutoff);
        harness.Options.MaxArchiveBytes = 500;

        var purged = await harness.SweepAsync();

        purged.Should().Be(2);
        harness.Source.Items.Select(item => item.Key).Should().Equal("young");
        harness.Snapshot.Should().Be(new DiskWatermarkStatusInfo(DiskWatermarkState.Unreachable, 1_200, null, 750, Now));
        harness.Logger.Levels.Should().Equal(LogLevel.Warning);
    }

    [Fact]
    public async Task PurgeBatchAsync_AfterAnUnreachableSweep_KeepsDrainingTowardsTheTarget()
    {
        var harness = new Harness().WithItems(100, 100, 100);
        harness.Options.MaxArchiveBytes = 300;
        harness.Status.Save(PassName, new DiskWatermarkStatusInfo(DiskWatermarkState.Unreachable, 2_000, null, 2_000, Now));

        var purged = await harness.SweepAsync();

        purged.Should().Be(1, "300 bytes sit at the limit but over the target of 270");
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public async Task PurgeBatchAsync_ADrainThatReachedItsTarget_StartsFromTheLimitsAgain()
    {
        var harness = new Harness().WithItems(100, 100);
        harness.Options.MaxArchiveBytes = 300;
        harness.Status.Save(PassName, new DiskWatermarkStatusInfo(DiskWatermarkState.Draining, 400, null, 130, Now));

        var purged = await harness.SweepAsync();

        purged.Should().Be(0);
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Within);
    }

    [Fact]
    public async Task PurgeBatchAsync_LimitsAtTheTopOfTheRange_SaturateInsteadOfOverflowing()
    {
        // A floor of long.MaxValue at a ratio of 0.5 has a target past the range; it is capped, so the
        // shortfall stays positive and the watermark is reported unreachable rather than met.
        var harness = new Harness().WithItems(100);
        harness.Options.MaxArchiveBytes = long.MaxValue;
        harness.Options.MinAvailableBytes = long.MaxValue;
        harness.Options.VolumePath = VolumePath;
        harness.Options.TargetFillRatio = 0.5;
        harness.Volume.AvailableBytes = 0;

        var purged = await harness.SweepAsync();

        purged.Should().Be(1);
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Unreachable);
        harness.Snapshot.RequiredBytes.Should().Be(long.MaxValue);
    }

    [Fact]
    public async Task PurgeBatchAsync_AFloorWithoutAPath_MeasuresNoVolume()
    {
        // The validator refuses this at startup; the pass still never measures a volume it has no path to.
        var harness = new Harness().WithItems(100);
        harness.Options.MinAvailableBytes = 1_000;

        var purged = await harness.SweepAsync();

        purged.Should().Be(0);
        harness.Volume.MeasuredPaths.Should().BeEmpty();
        harness.Snapshot!.AvailableBytes.Should().BeNull();
    }

    [Fact]
    public async Task PurgeBatchAsync_ANegativeSize_FailsTheSweepAndRecordsTheFailedMeasurement()
    {
        var harness = new Harness();
        harness.Source.Add("broken", -1, Cutoff.AddDays(-1));
        harness.Options.MaxArchiveBytes = 300;

        var act = () => harness.CreatePass().PurgeBatchAsync(10, Cutoff);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'broken'*");
        harness.Snapshot.Should().Be(new DiskWatermarkStatusInfo(DiskWatermarkState.MeasurementFailed, null, null, 0, Now));
    }

    [Fact]
    public async Task PurgeBatchAsync_ListingFails_RecordsTheFailedMeasurementAndRethrows()
    {
        var harness = new Harness();
        harness.Source.ListFailure = new IOException("archive offline");
        harness.Options.MaxArchiveBytes = 300;

        var act = () => harness.CreatePass().PurgeBatchAsync(10, Cutoff);

        await act.Should().ThrowAsync<IOException>();
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.MeasurementFailed);
    }

    [Fact]
    public async Task PurgeBatchAsync_CancelledBetweenDeletions_StopsBeforeTheNextOne()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = new Harness().WithItems(100, 100, 100);
        harness.Options.MaxArchiveBytes = 100;
        harness.Source.AfterDelete = _ => cancellation.Cancel();

        var act = () => harness.CreatePass().PurgeBatchAsync(10, Cutoff, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Source.Deleted.Should().ContainSingle();
    }

    [Fact]
    public async Task PurgeBatchAsync_DeletionFails_PropagatesTheFailure()
    {
        var harness = new Harness().WithItems(100, 100);
        harness.Options.MaxArchiveBytes = 100;
        harness.Source.DeleteFailure = new IOException("file locked");

        var act = () => harness.CreatePass().PurgeBatchAsync(10, Cutoff);

        await act.Should().ThrowAsync<IOException>();
        harness.Snapshot!.State.Should().Be(DiskWatermarkState.Draining);
    }

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        var source = new FakeDiskWatermarkSource();
        var volume = new FakeVolumeSpaceProvider();
        var planner = new DiskWatermarkPlanner();
        var options = Substitute.For<IOptionsMonitor<DiskWatermarkOptions>>();
        var status = new DiskWatermarkStatusStore();
        var clock = TimeProvider.System;
        var logger = NullLogger<DiskWatermarkRetentionPass<FakeDiskWatermarkSource>>.Instance;

        var constructors = new Action[]
        {
            () => _ = new DiskWatermarkRetentionPass<FakeDiskWatermarkSource>(source, volume, null!, options, status, clock, logger),
            () => _ = new DiskWatermarkRetentionPass<FakeDiskWatermarkSource>(null!, volume, planner, options, status, clock, logger),
            () => _ = new DiskWatermarkRetentionPass<FakeDiskWatermarkSource>(source, null!, planner, options, status, clock, logger),
            () => _ = new DiskWatermarkRetentionPass<FakeDiskWatermarkSource>(source, volume, planner, null!, status, clock, logger),
            () => _ = new DiskWatermarkRetentionPass<FakeDiskWatermarkSource>(source, volume, planner, options, null!, clock, logger),
            () => _ = new DiskWatermarkRetentionPass<FakeDiskWatermarkSource>(source, volume, planner, options, status, null!, logger),
            () => _ = new DiskWatermarkRetentionPass<FakeDiskWatermarkSource>(source, volume, planner, options, status, clock, null!),
        };

        constructors.Should().AllSatisfy(constructor => constructor.Should().Throw<ArgumentNullException>());
    }
}
