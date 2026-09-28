using Bsft.Core.Polling;
using Bsft.Retention.Tests.Fakes;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Retention.Tests;

public sealed class RetentionSweeperTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static (RetentionSweeper<RecordingRetentionPass> Sweeper, RecordingRetentionPass Pass, RecordingTimeProvider Clock)
        Build(Action<BsftRetentionOptions>? configure = null, ILogger<RetentionSweeper<RecordingRetentionPass>>? logger = null)
    {
        var pass = new RecordingRetentionPass();
        var clock = new RecordingTimeProvider(Now);

        var options = new BsftRetentionOptions { RetentionPeriod = Retention, MaxStartupStagger = TimeSpan.Zero };
        configure?.Invoke(options);

        var monitor = Substitute.For<IOptionsMonitor<BsftRetentionOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(options);

        var services = new ServiceCollection();
        services.AddScoped(_ => pass);
        var provider = services.BuildServiceProvider();

        var sweeper = new RetentionSweeper<RecordingRetentionPass>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            monitor,
            clock,
            logger ?? NullLogger<RetentionSweeper<RecordingRetentionPass>>.Instance);

        return (sweeper, pass, clock);
    }

    [Fact]
    public async Task SweepAsync_PassesTheConfiguredBatchSizeAndTheCutoffFromTheClock()
    {
        var (sweeper, pass, _) = Build(o => o.BatchSize = 25);
        pass.Backlog = 1;

        await sweeper.SweepAsync(Retention);

        pass.Calls.Should().ContainSingle();
        pass.Calls[0].BatchSize.Should().Be(25);
        pass.Calls[0].OlderThan.Should().Be(Now - Retention);
    }

    [Fact]
    public async Task SweepAsync_ShortBatch_StopsAndWaitsForTheInterval()
    {
        // A batch that came back short is the backlog reporting itself drained.
        var (sweeper, pass, _) = Build(o => o.BatchSize = 10);
        pass.Backlog = 4;

        var outcome = await sweeper.SweepAsync(Retention);

        outcome.Should().Be(PollPassOutcome.WaitForInterval);
        pass.Calls.Should().ContainSingle();
        pass.Backlog.Should().Be(0);
    }

    [Fact]
    public async Task SweepAsync_EmptyBacklog_MakesExactlyOneCall()
    {
        var (sweeper, pass, _) = Build(o => o.BatchSize = 10);

        var outcome = await sweeper.SweepAsync(Retention);

        outcome.Should().Be(PollPassOutcome.WaitForInterval);
        pass.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task SweepAsync_DrainsAFullBacklogAcrossBatchesWithinOneSweep()
    {
        var (sweeper, pass, _) = Build(o =>
        {
            o.BatchSize = 10;
            o.MaxBatchesPerSweep = 100;
        });
        pass.Backlog = 25;

        var outcome = await sweeper.SweepAsync(Retention);

        // Three batches: 10, 10, then a short 5 that ends the sweep.
        pass.Calls.Should().HaveCount(3);
        pass.Backlog.Should().Be(0);
        outcome.Should().Be(PollPassOutcome.WaitForInterval);
    }

    [Fact]
    public async Task SweepAsync_EveryBatchOfOneSweepUsesTheSameCutoff()
    {
        // The cutoff is computed once per sweep, so a long drain deletes the set it set out to delete
        // instead of a target creeping forward under it.
        var (sweeper, pass, clock) = Build(o => o.BatchSize = 10);
        pass.Backlog = 25;
        clock.Now = Now;

        await sweeper.SweepAsync(Retention);

        pass.Calls.Select(c => c.OlderThan).Distinct().Should().ContainSingle()
            .Which.Should().Be(Now - Retention);
    }

    [Fact]
    public async Task SweepAsync_BacklogOutlastsTheBatchBound_PollsAgain()
    {
        var (sweeper, pass, _) = Build(o =>
        {
            o.BatchSize = 10;
            o.MaxBatchesPerSweep = 2;
        });
        pass.Backlog = 100;

        var outcome = await sweeper.SweepAsync(Retention);

        outcome.Should().Be(PollPassOutcome.PollAgain);
        pass.Calls.Should().HaveCount(2);
        pass.Backlog.Should().Be(80);
    }

    [Fact]
    public async Task SweepAsync_PositiveBatchDelay_PacesBetweenBatchesButNotAfterTheLast()
    {
        var (sweeper, pass, clock) = Build(o =>
        {
            o.BatchSize = 10;
            o.BatchDelay = TimeSpan.FromSeconds(2);
        });
        pass.Backlog = 25;

        await sweeper.SweepAsync(Retention);

        // Three batches, so two gaps: the short final batch returns without pausing again.
        clock.Delays.Should().HaveCount(2);
        clock.Delays.Should().AllBeEquivalentTo(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SweepAsync_ZeroBatchDelay_NeverWaits()
    {
        var (sweeper, pass, clock) = Build(o =>
        {
            o.BatchSize = 10;
            o.BatchDelay = TimeSpan.Zero;
        });
        pass.Backlog = 25;

        await sweeper.SweepAsync(Retention);

        clock.Delays.Should().BeEmpty();
    }

    [Fact]
    public async Task SweepAsync_PassThrows_TheFailureSurfacesToTheLoop()
    {
        // The sweep does not swallow: PollLoop is what decides a failed pass runs again.
        var (sweeper, pass, _) = Build();
        pass.Failure = new InvalidOperationException("purge failed");

        var act = async () => await sweeper.SweepAsync(Retention);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("purge failed");
    }

    [Fact]
    public async Task RunLoopAsync_NoRetentionPeriod_SweepsNothing()
    {
        var (sweeper, pass, _) = Build(o => o.RetentionPeriod = null);
        pass.Backlog = 10;

        await sweeper.RunLoopAsync(new CancellationToken(canceled: true));

        pass.Calls.Should().BeEmpty();
        pass.Backlog.Should().Be(10);
    }

    [Fact]
    public async Task RunLoopAsync_SweepsThenStopsOnCancellation()
    {
        var (sweeper, pass, _) = Build(o =>
        {
            o.RetentionPeriod = TimeSpan.FromMinutes(1);
            o.BatchSize = 10;
        });
        pass.Backlog = 5;

        using var cts = new CancellationTokenSource();
        var loop = sweeper.RunLoopAsync(cts.Token);
        await WaitUntilAsync(loop, () => pass.Backlog == 0);
        await cts.CancelAsync();
        await loop;

        pass.Backlog.Should().Be(0);
    }

    [Fact]
    public async Task RunLoopAsync_StaggerCancelledBeforeItElapses_NeverSweeps()
    {
        var (sweeper, pass, _) = Build(o => o.MaxStartupStagger = TimeSpan.FromHours(1));
        pass.Backlog = 10;

        await sweeper.RunLoopAsync(new CancellationToken(canceled: true));

        pass.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunLoopAsync_PositiveStagger_WaitsTheDeterministicOffsetBeforeTheFirstSweep()
    {
        var maxStagger = TimeSpan.FromMinutes(10);
        var (sweeper, pass, clock) = Build(o =>
        {
            o.MaxStartupStagger = maxStagger;
            o.RetentionPeriod = TimeSpan.FromMinutes(1);
            o.BatchSize = 10;
        });
        pass.Backlog = 1;

        using var cts = new CancellationTokenSource();
        var loop = sweeper.RunLoopAsync(cts.Token);
        await WaitUntilAsync(loop, () => pass.Backlog == 0);
        await cts.CancelAsync();
        await loop;

        var expected = RetentionStagger.OffsetFor(
            typeof(RecordingRetentionPass).FullName!, maxStagger);
        clock.Delays.Should().NotBeEmpty();
        clock.Delays[0].Should().Be(expected);
    }

    [Fact]
    public async Task RunLoopAsync_SurvivesASweepFailureAndReportsIt()
    {
        var logger = Substitute.For<ILogger<RetentionSweeper<RecordingRetentionPass>>>();
        var (sweeper, pass, _) = Build(
            o => o.RetentionPeriod = TimeSpan.FromMilliseconds(1), logger);
        pass.Failure = new InvalidOperationException("boom");

        using var cts = new CancellationTokenSource();
        var loop = sweeper.RunLoopAsync(cts.Token);
        await WaitUntilAsync(loop, () => logger.ReceivedCalls().Any());
        await cts.CancelAsync();
        await loop;

        logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunLoopAsync_PassCancelledOnItsOwnToken_KeepsPolling()
    {
        // A command-timeout shape: the sweep is cancelled by a token of its own, not the stopping
        // token. The sweeper must keep polling rather than treat it as a shutdown.
        var logger = Substitute.For<ILogger<RetentionSweeper<RecordingRetentionPass>>>();
        var (sweeper, pass, _) = Build(
            o => o.RetentionPeriod = TimeSpan.FromMilliseconds(1), logger);
        pass.Failure = new OperationCanceledException();

        using var cts = new CancellationTokenSource();
        var loop = sweeper.RunLoopAsync(cts.Token);
        await WaitUntilAsync(loop, () => pass.Calls.Count >= 2);
        await cts.CancelAsync();

        var act = async () => await loop;

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_RunsViaHostedServiceLifecycle()
    {
        var (sweeper, pass, _) = Build(o => o.RetentionPeriod = TimeSpan.FromMinutes(1));

        await sweeper.StartAsync(CancellationToken.None);
        // Since Microsoft.Extensions.Hosting 10, StartAsync runs the whole of ExecuteAsync on the
        // thread pool, so a stop right after the start can come before the first sweep. The test waits
        // for it rather than relying on the synchronous start of earlier versions.
        SpinWait.SpinUntil(() => pass.Calls.Count > 0, TimeSpan.FromSeconds(10)).Should().BeTrue();
        await sweeper.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunLoopAsync_LogsWhatASweepDeleted()
    {
        var logger = Substitute.For<ILogger<RetentionSweeper<RecordingRetentionPass>>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var (sweeper, pass, _) = Build(o => o.BatchSize = 10, logger);
        pass.Backlog = 3;

        await sweeper.SweepAsync(Retention);

        logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_NullScopeFactory_Throws()
    {
        var monitor = Substitute.For<IOptionsMonitor<BsftRetentionOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(new BsftRetentionOptions());

        var act = () => new RetentionSweeper<RecordingRetentionPass>(
            null!, monitor, TimeProvider.System, NullLogger<RetentionSweeper<RecordingRetentionPass>>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new RetentionSweeper<RecordingRetentionPass>(
            Substitute.For<IServiceScopeFactory>(),
            null!,
            TimeProvider.System,
            NullLogger<RetentionSweeper<RecordingRetentionPass>>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        var monitor = Substitute.For<IOptionsMonitor<BsftRetentionOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(new BsftRetentionOptions());

        var act = () => new RetentionSweeper<RecordingRetentionPass>(
            Substitute.For<IServiceScopeFactory>(),
            monitor,
            null!,
            NullLogger<RetentionSweeper<RecordingRetentionPass>>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var monitor = Substitute.For<IOptionsMonitor<BsftRetentionOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(new BsftRetentionOptions());

        var act = () => new RetentionSweeper<RecordingRetentionPass>(
            Substitute.For<IServiceScopeFactory>(), monitor, TimeProvider.System, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    /// <summary>Waits until <paramref name="condition"/> holds, for as long as the loop is still running.</summary>
    /// <param name="loop">The running loop that is expected to bring the condition about.</param>
    /// <param name="condition">The state the loop is expected to reach.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <remarks>
    /// There is no deadline, so how long a loaded machine takes to get there decides nothing. What ends
    /// the wait early is the loop itself having ended, which fails the test instead of hanging it.
    /// </remarks>
    private static async Task WaitUntilAsync(Task loop, Func<bool> condition)
    {
        while (!condition())
        {
            if (loop.IsCompleted)
            {
                condition().Should().BeTrue("the loop ended before it reached the expected state");

                return;
            }

            await Task.Yield();
        }
    }
}
