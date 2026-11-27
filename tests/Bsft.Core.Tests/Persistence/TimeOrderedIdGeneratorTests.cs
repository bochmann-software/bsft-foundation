using Bsft.Core.Persistence;
using Bsft.Core.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Persistence;

/// <summary>
/// Tests for the TimeOrderedIdGenerator, verifying time-ordered id generation with strict
/// monotonicity, handling of clock changes, and node id management.
/// </summary>
public sealed class TimeOrderedIdGeneratorTests
{
    [Fact]
    public void NextId_SequentialCallsIncreaseStrictly_OneHundredThousandIds()
    {
        var generator = new TimeOrderedIdGenerator(0, TimeProvider.System);

        var ids = Enumerable.Range(0, 100_000).Select(_ => generator.NextId()).ToList();

        for (int i = 1; i < ids.Count; i++)
        {
            ids[i].Should().BeGreaterThan(ids[i - 1], because: $"id at index {i} should be > id at index {i - 1}");
        }
    }

    [Fact]
    public async Task NextId_MultipleThreads_AllIdsDistinctAndIncreasing()
    {
        var generator = new TimeOrderedIdGenerator(0, TimeProvider.System);
        var idsPerThread = 20_000;
        var threadCount = 16;

        var allIds = new System.Collections.Concurrent.ConcurrentBag<long>();
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < idsPerThread; i++)
                {
                    allIds.Add(generator.NextId());
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var sorted = allIds.OrderBy(x => x).ToList();

        // All ids should be distinct
        sorted.Distinct().Count().Should().Be(sorted.Count);

        // Sorted list should equal the original (i.e., merge sort of thread-local lists equals global sort)
        sorted.Should().BeInAscendingOrder();

        // All ids should be positive
        sorted.Should().AllSatisfy(id => id.Should().BeGreaterThan(0));
    }

    [Fact]
    public void NextId_MoreThanMaxSequencePerMillisecond_SequenceWrapsAndTimeAdvances()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);
        var generator = new TimeOrderedIdGenerator(0, fakeTime);

        var ids = new List<long>();
        for (int i = 0; i < TimeOrderedIdGenerator.MaxSequence + 50; i++)
        {
            ids.Add(generator.NextId());
        }

        var lastId = ids[^1];
        var (createdAt, nodeId, sequence) = TimeOrderedIdGenerator.Decompose(lastId);

        // The last id should have advanced the millisecond because we exceeded MaxSequence
        createdAt.Should().Be(TimeOrderedIdGenerator.Epoch.AddMilliseconds(1));
        nodeId.Should().Be(0);
        // Sequence should have wrapped within the new millisecond
        sequence.Should().BeLessThanOrEqualTo(TimeOrderedIdGenerator.MaxSequence);

        // All ids should still be strictly increasing
        for (int i = 1; i < ids.Count; i++)
        {
            ids[i].Should().BeGreaterThan(ids[i - 1]);
        }
    }

    [Fact]
    public void NextId_FrozenClockGenerates1025Ids_SequenceWrapsAndTimeAdvances()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);
        var generator = new TimeOrderedIdGenerator(0, fakeTime);

        var ids = new List<long>();
        for (int i = 0; i < 1025; i++)
        {
            ids.Add(generator.NextId());
        }

        // All ids should be distinct and increasing
        ids.Distinct().Count().Should().Be(ids.Count);
        ids.Should().BeInAscendingOrder();

        // The last id should have an advanced millisecond
        var (createdAt, _, _) = TimeOrderedIdGenerator.Decompose(ids[^1]);
        createdAt.Should().Be(TimeOrderedIdGenerator.Epoch.AddMilliseconds(1));
    }

    [Fact]
    public void NextId_ClockStepsBackwardByOneSecond_NextIdStillGreater()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch.AddMilliseconds(1000));
        var generator = new TimeOrderedIdGenerator(0, fakeTime);

        var id1 = generator.NextId();

        // Move clock backward by 1 second (1000 milliseconds)
        fakeTime.Advance(TimeSpan.FromMilliseconds(-1000));

        var id2 = generator.NextId();

        // id2 should still be greater than id1, despite the clock going back
        id2.Should().BeGreaterThan(id1);
    }

    [Fact]
    public void Decompose_RoundTrip_CreatedAtEqualsClockMilliseconds()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch.AddMilliseconds(12345));
        var generator = new TimeOrderedIdGenerator(42, fakeTime);

        var id = generator.NextId();
        var (createdAt, nodeId, sequence) = TimeOrderedIdGenerator.Decompose(id);

        createdAt.Should().Be(fakeTime.GetUtcNow());
        nodeId.Should().Be(42);
        // When counter = floor (12345 << 10), sequence is 0 (low 10 bits are 0)
        sequence.Should().Be(0);
    }

    [Fact]
    public void Decompose_WithNegativeId_ThrowsArgumentOutOfRangeException()
    {
        var act = () => TimeOrderedIdGenerator.Decompose(-1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("id");
    }

    [Fact]
    public void NextId_ClockBeyond41BitTimeCapacity_ThrowsInvalidOperationException()
    {
        // 41 bits of milliseconds = (2^41 - 1) milliseconds = 2,199,023,255,551 milliseconds
        // = approximately 69.7 years from epoch (2025-01-01)
        var epochPlusBeyondMax = TimeOrderedIdGenerator.Epoch
            .AddMilliseconds((1L << 41) + 1);

        var fakeTime = new FakeTimeProvider(epochPlusBeyondMax);
        var generator = new TimeOrderedIdGenerator(0, fakeTime);

        var act = () => generator.NextId();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*time-ordered id space is exhausted*");
    }

    [Fact]
    public void NextId_ClockBeforeEpoch_IdsAreNonNegative()
    {
        var beforeEpoch = TimeOrderedIdGenerator.Epoch.AddMilliseconds(-1000);
        var fakeTime = new FakeTimeProvider(beforeEpoch);
        var generator = new TimeOrderedIdGenerator(0, fakeTime);

        var id = generator.NextId();

        // Clock before epoch should use 0 for negative milliseconds, producing a valid positive id
        id.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Constructor_NodeIdNegative_ThrowsArgumentOutOfRangeException()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        var act = () => new TimeOrderedIdGenerator(-1, fakeTime);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("nodeId");
    }

    [Fact]
    public void Constructor_NodeIdAboveMaxNodeId_ThrowsArgumentOutOfRangeException()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        var act = () => new TimeOrderedIdGenerator(TimeOrderedIdGenerator.MaxNodeId + 1, fakeTime);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("nodeId");
    }

    [Fact]
    public void Constructor_NodeIdZero_Succeeds()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        var generator = new TimeOrderedIdGenerator(0, fakeTime);

        generator.NodeId.Should().Be(0);
    }

    [Fact]
    public void Constructor_NodeIdMaxNodeId_Succeeds()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        var generator = new TimeOrderedIdGenerator(TimeOrderedIdGenerator.MaxNodeId, fakeTime);

        generator.NodeId.Should().Be(TimeOrderedIdGenerator.MaxNodeId);
    }

    [Fact]
    public void Constructor_TimeProviderNull_ThrowsArgumentNullException()
    {
        var act = () => new TimeOrderedIdGenerator(0, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public void ForNode_SameNodeIdAndClock_ReturnsSameInstance()
    {
        var clock = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        var generator1 = TimeOrderedIdGenerator.ForNode(5, clock);
        var generator2 = TimeOrderedIdGenerator.ForNode(5, clock);

        generator2.Should().BeSameAs(generator1);
    }

    [Fact]
    public void ForNode_DifferentNodeIds_ReturnsDifferentInstances()
    {
        var clock = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        var generator1 = TimeOrderedIdGenerator.ForNode(5, clock);
        var generator2 = TimeOrderedIdGenerator.ForNode(6, clock);

        generator2.Should().NotBeSameAs(generator1);
    }

    [Fact]
    public void ForNode_DifferentClocks_ReturnsInstancesThatReadTheirOwnClock()
    {
        // The time field comes from the clock the caller passed, so a host's registered clock (or a
        // test's fake) decides the instant an id carries.
        var early = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch.AddDays(1));
        var late = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch.AddDays(2));

        var earlyGenerator = TimeOrderedIdGenerator.ForNode(5, early);
        var lateGenerator = TimeOrderedIdGenerator.ForNode(5, late);

        lateGenerator.Should().NotBeSameAs(earlyGenerator);
        TimeOrderedIdGenerator.Decompose(earlyGenerator.NextId()).CreatedAt.Should().Be(early.GetUtcNow());
        TimeOrderedIdGenerator.Decompose(lateGenerator.NextId()).CreatedAt.Should().Be(late.GetUtcNow());
    }

    [Fact]
    public void ForNode_WithoutAClock_ThrowsArgumentNullException()
    {
        var act = () => TimeOrderedIdGenerator.ForNode(5, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public void ForNode_DifferentNodeIdsAndSameFrozenClock_ProduceDisjointIdSets()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        // Create two instances with different node ids but same frozen clock
        var gen1 = new TimeOrderedIdGenerator(5, fakeTime);
        var gen2 = new TimeOrderedIdGenerator(6, fakeTime);

        var id1 = gen1.NextId();
        var id2 = gen2.NextId();

        // They should produce different ids even though the clock is frozen
        id1.Should().NotBe(id2);

        // Decompose to verify the node ids are embedded correctly
        var (_, node1, _) = TimeOrderedIdGenerator.Decompose(id1);
        var (_, node2, _) = TimeOrderedIdGenerator.Decompose(id2);

        node1.Should().Be(5);
        node2.Should().Be(6);
    }

    [Fact]
    public void ForNode_SameNodeIdAndSameFrozenClock_ProduceSameFirstId()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        // Two separate instances with same node id and same frozen clock
        var gen1 = new TimeOrderedIdGenerator(7, new FakeTimeProvider(TimeOrderedIdGenerator.Epoch));
        var gen2 = new TimeOrderedIdGenerator(7, new FakeTimeProvider(TimeOrderedIdGenerator.Epoch));

        var id1 = gen1.NextId();
        var id2 = gen2.NextId();

        // Same node id + same clock = same first id (documents multi-process limitation)
        id1.Should().Be(id2);
    }

    [Fact]
    public void ForNode_NodeIdNegative_ThrowsArgumentOutOfRangeException()
    {
        var act = () => TimeOrderedIdGenerator.ForNode(-1, TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("nodeId");
    }

    [Fact]
    public void ForNode_NodeIdAboveMaxNodeId_ThrowsArgumentOutOfRangeException()
    {
        var act = () => TimeOrderedIdGenerator.ForNode(TimeOrderedIdGenerator.MaxNodeId + 1, TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("nodeId");
    }

    [Fact]
    public void ForRandomNode_WithoutAClock_ThrowsArgumentNullException()
    {
        var act = () => TimeOrderedIdGenerator.ForRandomNode(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public void ForRandomNode_ReturnsInstanceWithNodeIdInRange()
    {
        var generator = TimeOrderedIdGenerator.ForRandomNode(new FakeTimeProvider(TimeOrderedIdGenerator.Epoch));

        generator.NodeId.Should().BeGreaterThanOrEqualTo(0)
            .And.BeLessThanOrEqualTo(TimeOrderedIdGenerator.MaxNodeId);
    }

    [Fact]
    public void ForRandomNode_SameClock_ReturnsConsistentInstance()
    {
        var clock = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);

        var gen1 = TimeOrderedIdGenerator.ForRandomNode(clock);
        var gen2 = TimeOrderedIdGenerator.ForRandomNode(clock);

        gen2.Should().BeSameAs(gen1);
    }

    [Fact]
    public async Task ForRandomNode_ClocksSeenInParallel_GetPairwiseDifferentNodeIds()
    {
        // Containers of one process can start at once, so the step to the next node id has to be atomic. Every
        // round releases all workers together, and a step lost between them hands two clocks one node id.
        const int workers = 8;
        const int rounds = 500;
        var nodeIds = new int[rounds, workers];
        using var startTogether = new Barrier(workers);

        var running = Enumerable.Range(0, workers).Select(worker => Task.Factory.StartNew(
            () =>
            {
                for (var round = 0; round < rounds; round++)
                {
                    startTogether.SignalAndWait();
                    nodeIds[round, worker] = TimeOrderedIdGenerator
                        .ForRandomNode(new FakeTimeProvider(TimeOrderedIdGenerator.Epoch)).NodeId;
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default));
        await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(30));

        for (var round = 0; round < rounds; round++)
        {
            var current = round;
            Enumerable.Range(0, workers).Select(worker => nodeIds[current, worker]).Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void ForRandomNode_MoreClocksThanNodeIds_StaysInRange()
    {
        // Past 4096 clocks a node id repeats. Whether two of these clocks share one is not asserted, because
        // other tests that run in parallel move the same process-wide counter.
        var nodeIds = Enumerable.Range(0, TimeOrderedIdGenerator.MaxNodeId + 10)
            .Select(_ => TimeOrderedIdGenerator.ForRandomNode(new FakeTimeProvider(TimeOrderedIdGenerator.Epoch)).NodeId)
            .ToArray();

        nodeIds.Should().OnlyContain(nodeId => nodeId >= 0 && nodeId <= TimeOrderedIdGenerator.MaxNodeId);
    }

    [Fact]
    public void ForRandomNode_TwoClocksAtTheSameInstant_NeverProduceTheSameId()
    {
        // Two containers in one process with clocks of their own, no configured node id and a shared
        // database: each generator's counter starts at zero, so only the node id can keep them apart.
        var instant = TimeOrderedIdGenerator.Epoch.AddDays(1);
        var first = TimeOrderedIdGenerator.ForRandomNode(new FakeTimeProvider(instant));
        var second = TimeOrderedIdGenerator.ForRandomNode(new FakeTimeProvider(instant));

        var ids = Enumerable.Range(0, 2000).SelectMany(_ => new[] { first.NextId(), second.NextId() }).ToArray();

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Epoch_IsJanuary1st2025Utc()
    {
        TimeOrderedIdGenerator.Epoch.Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void MaxNodeId_IsValidValue()
    {
        TimeOrderedIdGenerator.MaxNodeId.Should().Be(4095); // (1 << 12) - 1
    }

    [Fact]
    public void MaxSequence_IsValidValue()
    {
        TimeOrderedIdGenerator.MaxSequence.Should().Be(1023); // (1 << 10) - 1
    }

    [Fact]
    public void NextId_AllIdsPositive()
    {
        var generator = new TimeOrderedIdGenerator(0, TimeProvider.System);

        for (int i = 0; i < 10_000; i++)
        {
            var id = generator.NextId();
            id.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void NextId_WithDifferentNodeIds_DecomposeShowsCorrectNodeId()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);
        var generator = new TimeOrderedIdGenerator(100, fakeTime);

        var id = generator.NextId();
        var (_, nodeId, _) = TimeOrderedIdGenerator.Decompose(id);

        nodeId.Should().Be(100);
    }

    [Fact]
    public void NextId_MultipleIdsInSameMillisecond_SequencesIncrement()
    {
        var fakeTime = new FakeTimeProvider(TimeOrderedIdGenerator.Epoch);
        var generator = new TimeOrderedIdGenerator(0, fakeTime);

        var ids = Enumerable.Range(0, 10).Select(_ => generator.NextId()).ToList();

        var sequences = ids.Select(id => TimeOrderedIdGenerator.Decompose(id).Sequence).ToList();

        // Counter starts at 0 but first next() call increments to 1, so first sequence is 1
        sequences.Should().Equal(Enumerable.Range(1, 10));
    }
}
