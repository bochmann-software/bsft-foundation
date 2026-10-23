using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines;

public sealed class EnrichmentStageWorkerTests
{
    [Fact]
    public async Task EnrichmentStageWorker_WhenTheEnricherSucceeds_PassesTheEnrichedItemOn()
    {
        using var host = new PipelineHost(withEnrichment: true);
        await host.StartAsync();

        host.EnrichmentStage.TryEnqueue(new SampleItem(1)).Should().BeTrue();
        await host.StopAsync();

        host.Committer.Items.Should().ContainSingle().Which.Should().Be(new SampleItem(1, "enriched"));
        host.Counters.EnrichmentFailed.Should().Be(0);
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenTheEnricherReturnsNull_CountsAFailureAndPassesTheRawItemOn()
    {
        using var host = new PipelineHost(withEnrichment: true);
        host.Enricher.Behavior = static (_, _) => ValueTask.FromResult<SampleItem?>(null);
        await host.StartAsync();

        host.EnrichmentStage.TryEnqueue(new SampleItem(1));
        await host.StopAsync();

        host.Committer.Items.Should().ContainSingle().Which.Should().Be(new SampleItem(1));
        host.Counters.EnrichmentFailed.Should().Be(1);
        host.Stage.Messages.Should().Contain(message => message.Contains("returned no item", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenTheEnricherFaults_CountsAFailureLogsItAndPassesTheRawItemOn()
    {
        using var host = new PipelineHost(withEnrichment: true);
        host.Enricher.Behavior = static (_, _) => throw new InvalidOperationException("the lookup failed");
        await host.StartAsync();

        host.EnrichmentStage.TryEnqueue(new SampleItem(1));
        await host.StopAsync();

        host.Committer.Items.Should().ContainSingle().Which.Should().Be(new SampleItem(1));
        host.Counters.EnrichmentFailed.Should().Be(1);
        host.Stage.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenTheEnricherIgnoresItsTokenPastTheTimeout_GivesUpAndPassesTheRawItemOn()
    {
        using var host = new PipelineHost(options => options.EnrichmentTimeout = TimeSpan.FromSeconds(3), withEnrichment: true);
        var neverFinishes = new TaskCompletionSource<SampleItem?>();
        var started = new TaskCompletionSource();
        host.Enricher.Behavior = (_, _) =>
        {
            started.TrySetResult();
            return new ValueTask<SampleItem?>(neverFinishes.Task);
        };
        await host.StartAsync();

        host.EnrichmentStage.TryEnqueue(new SampleItem(1));
        await started.Task;
        host.Clock.Advance(TimeSpan.FromSeconds(3));
        await host.StopAsync();

        host.Committer.Items.Should().ContainSingle().Which.Should().Be(new SampleItem(1));
        host.Counters.EnrichmentFailed.Should().Be(1);
        host.Stage.Messages.Should().Contain(message => message.Contains("given up on", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenTheEnricherHonorsItsTokenAtTheTimeout_ReportsItAsGivenUpOn()
    {
        using var host = new PipelineHost(options => options.EnrichmentTimeout = TimeSpan.FromSeconds(3), withEnrichment: true);
        var started = new TaskCompletionSource();
        host.Enricher.Behavior = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        };
        await host.StartAsync();

        host.EnrichmentStage.TryEnqueue(new SampleItem(1));
        await started.Task;
        host.Clock.Advance(TimeSpan.FromSeconds(3));
        await host.StopAsync();

        host.Stage.Messages.Should().Contain(message => message.Contains("given up on", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenTheStopBudgetPasses_PassesTheRestOnRawAndLogsOnce()
    {
        using var host = new PipelineHost(options => options.EnrichmentOnStopTimeout = TimeSpan.FromSeconds(4), withEnrichment: true);
        var neverFinishes = new TaskCompletionSource<SampleItem?>();
        var started = new TaskCompletionSource();
        host.Enricher.Behavior = (_, _) =>
        {
            started.TrySetResult();
            return new ValueTask<SampleItem?>(neverFinishes.Task);
        };
        await host.StartAsync();
        foreach (var id in Enumerable.Range(1, 4))
            host.EnrichmentStage.TryEnqueue(new SampleItem(id));
        await started.Task;

        var stop = host.StopAsync();
        host.Clock.Advance(TimeSpan.FromSeconds(4));
        await stop;

        host.Committer.Items.Select(item => item.Id).Should().BeEquivalentTo([1, 2, 3, 4]);
        host.Counters.EnrichmentFailed.Should().Be(1);
        host.Counters.EnrichmentSkipped.Should().Be(3);
        host.Counters.Failed.Should().Be(0);
        host.Stage.Messages.Count(message => message.Contains("stop budget", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenTheStageUsesTheHostsWholeShutdownTime_TheWriterStillFlushesWhatTheStagePassedOn()
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
        host.EnrichmentStage.TryEnqueue(new SampleItem(1));
        host.EnrichmentStage.TryEnqueue(new SampleItem(2));
        await started.Task;
        using var shutdown = new CancellationTokenSource();

        var stop = host.StopAsync(shutdown.Token);
        await shutdown.CancelAsync();
        await stop;

        host.Committer.Items.Select(item => item.Id).Should().BeEquivalentTo([1, 2]);
        host.Counters.Failed.Should().Be(0);
    }

    [Fact]
    public void EnrichmentStageWorker_WhenTheStageIsFull_PassesTheItemOnRawAndCountsTheSkip()
    {
        using var host = new PipelineHost(options => options.EnrichmentCapacity = 1, withEnrichment: true);

        var results = new[] { 1, 2, 3 }.Select(id => host.EnrichmentStage.TryEnqueue(new SampleItem(id))).ToArray();

        results.Should().Equal(true, false, false);
        host.Counters.EnrichmentSkipped.Should().Be(2);
        host.Counters.Enqueued.Should().Be(2);
    }

    [Fact]
    public void EnrichmentStageWorker_WhenTheItemIsNull_Throws()
    {
        using var host = new PipelineHost(withEnrichment: true);

        var act = () => host.EnrichmentStage.TryEnqueue(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenStoppedBeforeItStarted_ReturnsAndSendsLaterItemsOnRaw()
    {
        using var host = new PipelineHost(withEnrichment: true);

        await host.StopAsync();

        host.EnrichmentStage.TryEnqueue(new SampleItem(1)).Should().BeFalse();
        host.Counters.EnrichmentSkipped.Should().Be(1);
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenManyProducersRace_EnrichesOrPassesOnEveryAcceptedItem()
    {
        using var host = new PipelineHost(
            options =>
            {
                options.Capacity = 5_000;
                options.EnrichmentCapacity = 100;
            },
            withEnrichment: true);
        await host.StartAsync();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
        {
            for (var index = 0; index < 250; index++)
                host.EnrichmentStage.TryEnqueue(new SampleItem((producer * 250) + index));
        })));
        await host.StopAsync();

        host.Committer.Items.Select(item => item.Id).Distinct().Should().HaveCount(2_000);
    }

    [Fact]
    public void EnrichmentStageWorker_WhenDisposedBeforeItStarted_DoesNotThrow()
    {
        using var host = new PipelineHost(withEnrichment: true);
        var stage = host.Services[1];

        var act = () =>
        {
            ((IDisposable)stage).Dispose();
            ((IDisposable)stage).Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenDisposedWhileAnEnrichmentRuns_GivesUpOnItAndReleasesTheSourcesAfterwards()
    {
        using var host = new PipelineHost(withEnrichment: true);
        var neverFinishes = new TaskCompletionSource<SampleItem?>();
        var started = new TaskCompletionSource();
        host.Enricher.Behavior = (_, _) =>
        {
            started.TrySetResult();
            return new ValueTask<SampleItem?>(neverFinishes.Task);
        };
        var stage = host.Services[1];
        await stage.StartAsync(CancellationToken.None);
        host.EnrichmentStage.TryEnqueue(new SampleItem(1));
        await started.Task;

        var stop = stage.StopAsync(CancellationToken.None);
        ((IDisposable)stage).Dispose();
        await stop;

        host.Counters.EnrichmentFailed.Should().Be(1);
    }

    [Fact]
    public void EnrichmentStageWorker_WhenTheOptionsOrTheClockAreNull_Throws()
    {
        using var host = new PipelineHost();

        var nullOptions = () => Create(host, null, host.Clock);
        var nullClock = () => Create(host, host.Get<Microsoft.Extensions.Options.IOptionsMonitor<BsftBoundedPipelineOptions>>(), null);

        nullOptions.Should().Throw<ArgumentNullException>();
        nullClock.Should().Throw<ArgumentNullException>();
    }

    private static EnrichmentStageWorker<SampleItem> Create(
        PipelineHost host,
        Microsoft.Extensions.Options.IOptionsMonitor<BsftBoundedPipelineOptions>? options,
        TimeProvider? clock) =>
        new(
            host.Buffer,
            host.Enricher,
            host.CounterSink,
            new OperationRacer(host.Clock),
            options!,
            clock!,
            host.Stage);

    [Fact]
    public async Task EnrichmentStageWorker_WhenDisposedWithoutAStop_HandsTheQueueOnAndEnds()
    {
        using var host = new PipelineHost(withEnrichment: true);
        var stage = (EnrichmentStageWorker<SampleItem>)host.Services[1];
        var neverFinishes = new TaskCompletionSource<SampleItem?>();
        var started = new TaskCompletionSource();
        host.Enricher.Behavior = (_, _) =>
        {
            started.TrySetResult();
            return new ValueTask<SampleItem?>(neverFinishes.Task);
        };
        await stage.StartAsync(CancellationToken.None);
        host.EnrichmentStage.TryEnqueue(new SampleItem(1));
        host.EnrichmentStage.TryEnqueue(new SampleItem(2));
        await started.Task;

        stage.Dispose();
        await stage.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        host.Counters.Enqueued.Should().Be(2);
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenStoppedAfterItWasDisposed_WaitsForTheWorkerAndReturns()
    {
        using var host = new PipelineHost(withEnrichment: true);
        var stage = host.Services[1];
        await stage.StartAsync(CancellationToken.None);

        ((IDisposable)stage).Dispose();
        await stage.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        host.EnrichmentStage.TryEnqueue(new SampleItem(1)).Should().BeFalse();
    }

    [Fact]
    public async Task EnrichmentStageWorker_WhenStoppedBeforeItStarted_CompletesItsCompletionSignal()
    {
        using var host = new PipelineHost(withEnrichment: true);
        var stage = (EnrichmentStageWorker<SampleItem>)host.Services[1];

        await stage.StopAsync(CancellationToken.None);

        stage.Completion.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void EnrichmentStageWorker_WhenDisposedBeforeItStarted_CompletesItsCompletionSignal()
    {
        using var host = new PipelineHost(withEnrichment: true);
        var stage = (EnrichmentStageWorker<SampleItem>)host.Services[1];

        stage.Dispose();

        stage.Completion.IsCompletedSuccessfully.Should().BeTrue();
    }
}
