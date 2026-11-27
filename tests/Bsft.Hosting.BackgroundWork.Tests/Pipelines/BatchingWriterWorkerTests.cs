using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines;

public sealed class BatchingWriterWorkerTests
{
    [Fact]
    public async Task BatchingWriterWorker_WithAPendingStartGate_CommitsNothingUntilItOpens()
    {
        var gate = new ControllableStartGate();
        using var host = new PipelineHost(options => options.MaxBatchSize = 1, gate: gate);
        host.Buffer.TryEnqueue(new SampleItem(1));

        await host.StartAsync();
        await gate.Awaited;

        host.Committer.Batches.Should().BeEmpty();
        gate.Open();
        await host.Committer.WaitForBatchesAsync(1);
        await host.StopAsync();

        host.Committer.Batches.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchingWriterWorker_StoppedWhileAStartGateIsPending_CommitsNothingNotEvenTheFinalFlush()
    {
        var gate = new ControllableStartGate();
        using var host = new PipelineHost(gate: gate);
        host.Buffer.TryEnqueue(new SampleItem(1));

        await host.StartAsync();
        await gate.Awaited;
        await host.StopAsync();

        host.Committer.Batches.Should().BeEmpty();
        host.Writer.Entries.Should().NotContain(entry => entry.Level == LogLevel.Error);
        host.Counters.Committed.Should().Be(0);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenItemsExceedTheBatchSize_CommitsThemInBatchesOfAtMostThatSize()
    {
        using var host = new PipelineHost(options => options.MaxBatchSize = 2);
        foreach (var id in Enumerable.Range(1, 6))
            host.Buffer.TryEnqueue(new SampleItem(id));

        await host.StartAsync();
        await host.Committer.WaitForBatchesAsync(3);
        await host.StopAsync();

        host.Committer.Batches.Select(batch => string.Join(",", batch.Select(item => item.Id))).Should()
            .Equal("1,2", "3,4", "5,6");
        host.Counters.Committed.Should().Be(6);
        host.Counters.FailedBatches.Should().Be(0);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheIntervalHasNotPassed_HoldsTheBatchAndCommitsItWhenItHas()
    {
        using var host = new PipelineHost(options => options.FlushInterval = TimeSpan.FromSeconds(1));
        await host.StartAsync();

        host.Buffer.TryEnqueue(new SampleItem(1));
        await host.Clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        host.Clock.Advance(TimeSpan.FromMilliseconds(999));
        host.Committer.Batches.Should().BeEmpty();
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await host.Committer.WaitForBatchesAsync(1);

        host.Committer.Batches.Should().ContainSingle().Which.Should().HaveCount(1);
        await host.StopAsync();
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheBatchIsFullBeforeTheInterval_CommitsWithoutWaitingForIt()
    {
        using var host = new PipelineHost(options =>
        {
            options.FlushInterval = TimeSpan.FromHours(1);
            options.MaxBatchSize = 3;
        });
        foreach (var id in Enumerable.Range(1, 3))
            host.Buffer.TryEnqueue(new SampleItem(id));

        await host.StartAsync();
        await host.Committer.WaitForBatchesAsync(1);

        host.Committer.Batches.Should().ContainSingle().Which.Should().HaveCount(3);
        await host.StopAsync();
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheHostStopsBeforeTheIntervalPasses_CommitsTheOpenBatchAtOnce()
    {
        using var host = new PipelineHost(options => options.FlushInterval = TimeSpan.FromMinutes(30));
        await host.StartAsync();

        host.Buffer.TryEnqueue(new SampleItem(1));
        host.Buffer.TryEnqueue(new SampleItem(2));
        await host.StopAsync();

        host.Committer.Batches.Should().ContainSingle().Which.Should().HaveCount(2);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenABatchFails_LogsCountsAndKeepsCommittingTheNext()
    {
        using var host = new PipelineHost(options => options.MaxBatchSize = 2);
        host.Committer.Behavior = (number, _) => number == 1
            ? Task.FromException(new InvalidOperationException("the store is down"))
            : Task.CompletedTask;
        foreach (var id in Enumerable.Range(1, 4))
            host.Buffer.TryEnqueue(new SampleItem(id));

        await host.StartAsync();
        await host.Committer.WaitForBatchesAsync(2);
        await host.StopAsync();

        host.Counters.Failed.Should().Be(2);
        host.Counters.FailedBatches.Should().Be(1);
        host.Counters.Committed.Should().Be(2);
        host.Writer.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheCommitterThrowsBeforeReturningATask_IsolatesTheBatch()
    {
        using var host = new PipelineHost(options => options.MaxBatchSize = 1);
        host.Committer.Behavior = static (_, _) => throw new InvalidOperationException("synchronous");
        host.Buffer.TryEnqueue(new SampleItem(1));

        await host.StartAsync();
        await host.Committer.WaitForBatchesAsync(1);
        await host.StopAsync();

        host.Counters.FailedBatches.Should().Be(1);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheHostStops_FlushesWhatIsStillQueued()
    {
        using var host = new PipelineHost(options =>
        {
            options.FlushInterval = TimeSpan.FromMinutes(5);
            options.MaxBatchSize = 2;
        });
        await host.StartAsync();
        foreach (var id in Enumerable.Range(1, 5))
            host.Buffer.TryEnqueue(new SampleItem(id));

        await host.StopAsync();

        host.Committer.Items.Select(item => item.Id).Should().BeEquivalentTo([1, 2, 3, 4, 5]);
        host.Counters.Committed.Should().Be(5);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenAFullBatchIsStillQueuedAtTheStop_FlushesItAsABatchOfTheMaximumSize()
    {
        using var host = new PipelineHost(options =>
        {
            options.FlushInterval = TimeSpan.FromHours(1);
            options.MaxBatchSize = 2;
            options.Capacity = 8;
        });
        var gate = new TaskCompletionSource();
        host.Committer.Behavior = (number, _) => number == 1 ? gate.Task : Task.CompletedTask;
        foreach (var id in Enumerable.Range(1, 2))
            host.Buffer.TryEnqueue(new SampleItem(id));
        await host.StartAsync();
        await host.Committer.WaitForBatchesAsync(1);
        foreach (var id in Enumerable.Range(3, 3))
            host.Buffer.TryEnqueue(new SampleItem(id));

        var stop = host.StopAsync();
        gate.SetResult();
        await stop;

        host.Committer.Batches.Select(batch => string.Join(",", batch.Select(item => item.Id))).Should()
            .Equal("1,2", "3,4", "5");
        host.Counters.Committed.Should().Be(5);
        host.Counters.FailedBatches.Should().Be(0);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheFinalFlushOutlivesTheDeadline_AbandonsTheCommitAndCountsTheRest()
    {
        using var host = new PipelineHost(options =>
        {
            options.MaxBatchSize = 1;
            options.FlushOnStopTimeout = TimeSpan.FromSeconds(7);
        });
        var neverFinishes = new TaskCompletionSource();
        host.Committer.Behavior = (_, _) => neverFinishes.Task;
        foreach (var id in Enumerable.Range(1, 3))
            host.Buffer.TryEnqueue(new SampleItem(id));
        await host.StartAsync();
        await host.Committer.WaitForBatchesAsync(1);

        var stop = host.StopAsync();
        host.Clock.Advance(TimeSpan.FromSeconds(7));
        await stop;

        host.Counters.Failed.Should().Be(3);
        host.Counters.FailedBatches.Should().Be(1);
        host.Writer.Messages.Should().Contain(message => message.Contains("stop deadline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheHostsStopTokenIsAlreadyCancelled_StillGivesTheFlushItsOwnDeadline()
    {
        using var host = new PipelineHost(options => options.FlushOnStopTimeout = TimeSpan.FromSeconds(7));
        var gate = new TaskCompletionSource();
        host.Committer.Behavior = (number, _) => number == 1 ? gate.Task : Task.CompletedTask;
        host.Buffer.TryEnqueue(new SampleItem(1));
        await host.StartAsync();
        await host.Clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await host.Committer.WaitForBatchesAsync(1);
        host.Buffer.TryEnqueue(new SampleItem(2));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var stop = host.StopAsync(cancelled.Token);
        stop.IsCompleted.Should().BeFalse();
        host.Clock.Advance(TimeSpan.FromSeconds(7));
        await stop;

        host.Counters.Failed.Should().Be(2);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheHostsStopTokenIsAlreadyCancelled_StillFlushesWithinTheDeadline()
    {
        using var host = new PipelineHost();
        await host.StartAsync();
        host.Buffer.TryEnqueue(new SampleItem(1));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await host.StopAsync(cancelled.Token);

        host.Committer.Items.Should().ContainSingle();
        host.Counters.Failed.Should().Be(0);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenProducersRaceTheWorker_CommitsEveryAcceptedItemOnce()
    {
        using var host = new PipelineHost(options =>
        {
            options.Capacity = 5_000;
            options.MaxBatchSize = 64;
        });
        await host.StartAsync();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
        {
            for (var index = 0; index < 500; index++)
                host.Buffer.TryEnqueue(new SampleItem((producer * 500) + index));
        })));
        await host.StopAsync();

        host.Counters.Enqueued.Should().Be(4_000);
        host.Counters.Committed.Should().Be(4_000);
        host.Committer.Items.Select(item => item.Id).Distinct().Should().HaveCount(4_000);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenStoppedBeforeItStarted_CompletesAndRefusesFurtherItems()
    {
        using var host = new PipelineHost();

        await host.StopAsync();

        host.Buffer.TryEnqueue(new SampleItem(1)).Should().BeFalse();
        host.Counters.Dropped.Should().Be(1);
    }

    [Fact]
    public void BatchingWriterWorker_WhenDisposedTwice_DoesNotThrow()
    {
        using var host = new PipelineHost();
        var worker = host.Services[0];

        var act = () =>
        {
            ((IDisposable)worker).Dispose();
            ((IDisposable)worker).Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenDisposedWhileTheFinalFlushRuns_NeitherFaultsNorCommitsAfterwards()
    {
        using var host = new PipelineHost(options => options.MaxBatchSize = 1);
        var gate = new TaskCompletionSource();
        host.Committer.Behavior = (_, _) => gate.Task;
        foreach (var id in Enumerable.Range(1, 3))
            host.Buffer.TryEnqueue(new SampleItem(id));
        var worker = host.Services[0];
        await worker.StartAsync(CancellationToken.None);
        await host.Committer.WaitForBatchesAsync(1);

        var stop = worker.StopAsync(CancellationToken.None);
        ((IDisposable)worker).Dispose();
        gate.SetResult();
        await stop;

        host.Committer.Batches.Should().HaveCount(1);
    }

    [Fact]
    public void BatchingWriterWorker_WhenTheOptionsAreNull_Throws()
    {
        using var host = new PipelineHost();

        var act = () => new BatchingWriterWorker<SampleItem>(
            host.Get<IBoundedBufferSource<SampleItem>>(),
            host.Get<IBatchCommitRunner<SampleItem>>(),
            host.CounterSink,
            null!,
            host.Clock,
            host.Writer);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenDisposedWithoutAStop_NeverCallsTheCommitterAndCountsTheQueue()
    {
        using var host = new PipelineHost();
        var worker = host.Services[0];
        await worker.StartAsync(CancellationToken.None);
        host.Buffer.TryEnqueue(new SampleItem(1));
        host.Buffer.TryEnqueue(new SampleItem(2));
        await host.Clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));

        ((IDisposable)worker).Dispose();
        await worker.StopAsync(CancellationToken.None);

        host.Committer.Batches.Should().BeEmpty();
        host.Counters.Failed.Should().Be(2);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheCompletionWakesTheLoopDuringDispose_NeverCommitsTheBatchItHolds()
    {
        using var host = new PipelineHost();
        var source = new WakingSource(host.Get<IBoundedBufferSource<SampleItem>>(), host.Committer);
        using var worker = new BatchingWriterWorker<SampleItem>(
            source,
            host.Get<IBatchCommitRunner<SampleItem>>(),
            host.CounterSink,
            host.Get<IOptionsMonitor<BsftBoundedPipelineOptions>>(),
            host.Clock,
            host.Writer);
        await worker.StartAsync(CancellationToken.None);
        host.Buffer.TryEnqueue(new SampleItem(1));
        await host.Clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        await source.ItemRead.WaitAsync(TimeSpan.FromSeconds(30));

        worker.Dispose();
        await worker.StopAsync(CancellationToken.None);

        host.Committer.Batches.Should().BeEmpty();
    }

    /// <summary>A reader that reports the first item taken through it.</summary>
    private sealed class SignallingReader(
        System.Threading.Channels.ChannelReader<SampleItem> inner,
        TaskCompletionSource itemRead,
        ManualResetEventSlim loopWaitedAgain) : System.Threading.Channels.ChannelReader<SampleItem>
    {
        private int _waits;

        public override Task Completion => inner.Completion;

        public override bool CanCount => inner.CanCount;

        public override int Count => inner.Count;

        public override bool TryRead(out SampleItem item)
        {
            var read = inner.TryRead(out item!);
            if (read)
                itemRead.TrySetResult();

            return read;
        }

        /// <remarks>
        /// The first wait starts the loop, the second is the batch window that the disposal wakes, and the
        /// third is the loop coming back for more once it has dealt with the batch it held, which is after
        /// the commit it must not make.
        /// </remarks>
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _waits) >= 3)
                loopWaitedAgain.Set();

            return inner.WaitToReadAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Holds the thread that completes the buffer until the woken loop has had its chance to commit,
    /// so a disposal that completes the buffer before it cancels the deadline is caught every time.
    /// </summary>
    private sealed class WakingSource : IBoundedBufferSource<SampleItem>
    {
        private readonly IBoundedBufferSource<SampleItem> _inner;
        private readonly RecordingBatchCommitter _committer;
        private readonly TaskCompletionSource _itemRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _loopWaitedAgain = new(false);

        public WakingSource(IBoundedBufferSource<SampleItem> inner, RecordingBatchCommitter committer)
        {
            _inner = inner;
            _committer = committer;
            Reader = new SignallingReader(inner.Reader, _itemRead, _loopWaitedAgain);
        }

        /// <summary>Gets a task that completes once the loop has taken an item off the buffer.</summary>
        public Task ItemRead => _itemRead.Task;

        public System.Threading.Channels.ChannelReader<SampleItem> Reader { get; }

        public void Complete()
        {
            _inner.Complete();

            // The loop is back for more only after it has dealt with the batch it held, so a commit that
            // the disposal should have prevented has happened by the time this returns.
            _loopWaitedAgain.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheHostStopsEveryServiceAtOnce_StillFlushesWhatTheStagePassedOn()
    {
        using var host = new PipelineHost(withEnrichment: true);
        var neverFinishes = new TaskCompletionSource<SampleItem?>();
        var started = new TaskCompletionSource();
        host.Enricher.Behavior = (_, _) =>
        {
            started.TrySetResult();
            return new ValueTask<SampleItem?>(neverFinishes.Task);
        };
        await host.StartAsync();
        foreach (var id in Enumerable.Range(1, 3))
            host.EnrichmentStage.TryEnqueue(new SampleItem(id));
        await started.Task;

        var stop = host.StopConcurrentlyAsync();
        await host.Clock.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        await stop;

        host.Committer.Items.Select(item => item.Id).Should().BeEquivalentTo([1, 2, 3]);
        host.Counters.Dropped.Should().Be(0);
    }

    [Fact]
    public async Task BatchingWriterWorker_WhenTheStageNeverFinishes_StopsWithoutItAfterTheCombinedBudget()
    {
        using var host = new PipelineHost(withEnrichment: true);
        await host.StartAsync();
        host.Buffer.TryEnqueue(new SampleItem(1));

        var stop = host.Services[0].StopAsync(CancellationToken.None);
        await host.Clock.WaitForTimerAsync(TimeSpan.FromSeconds(15));
        host.Clock.Advance(TimeSpan.FromSeconds(15));
        await stop.WaitAsync(TimeSpan.FromSeconds(30));

        host.Committer.Items.Should().ContainSingle();
        host.Writer.Messages.Should().Contain(message => message.Contains("did not finish in time", StringComparison.Ordinal));
    }
}
