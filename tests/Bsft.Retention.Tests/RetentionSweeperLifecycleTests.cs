using Bsft.Core.Polling;
using Bsft.Retention.DependencyInjection;
using Bsft.Retention.Tests.Fakes;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Retention.Tests;

/// <summary>
/// How the sweeper takes part in the host's lifecycle: when its first sweep may begin, and how it
/// stops.
/// </summary>
public sealed class RetentionSweeperLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static RetentionSweeper<TPass> Build<TPass>(
        TPass pass,
        TimeProvider clock,
        RecordingLogger<RetentionSweeper<TPass>> logger,
        Action<BsftRetentionOptions>? configure = null)
        where TPass : class, IRetentionPass
    {
        var options = new BsftRetentionOptions { RetentionPeriod = Retention, MaxStartupStagger = TimeSpan.Zero };
        configure?.Invoke(options);

        var monitor = Substitute.For<IOptionsMonitor<BsftRetentionOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(options);

        var services = new ServiceCollection();
        services.AddScoped(_ => pass);
        var provider = services.BuildServiceProvider();

        return new RetentionSweeper<TPass>(provider.GetRequiredService<IServiceScopeFactory>(), monitor, clock, logger);
    }

    private static Task WaitingForStartAsync<TPass>(RecordingLogger<RetentionSweeper<TPass>> logger)
        where TPass : class, IRetentionPass =>
        logger.WrittenAsync(entry => entry.Message.Contains("waits for the host", StringComparison.Ordinal))
            .WaitAsync(Patience);

    [Fact]
    public async Task StartedAsync_AnotherHostedServiceStillStarting_SweepsOnlyOnceItHasStarted()
    {
        var pass = new RecordingRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<RecordingRetentionPass>>();
        var blocker = new BlockingHostedService();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(new TimerSignallingTimeProvider(Now));
                services.AddSingleton<ILogger<RetentionSweeper<RecordingRetentionPass>>>(logger);
                services.AddScoped(_ => pass);
                services.AddBsftRetention<RecordingRetentionPass>(o =>
                {
                    o.RetentionPeriod = Retention;
                    o.MaxStartupStagger = TimeSpan.Zero;
                });

                // Registered after the sweeper, so the sweeper's own start has run while this one blocks.
                services.AddHostedService(_ => blocker);
            })
            .Build();

        var start = host.StartAsync();
        await blocker.Entered.WaitAsync(Patience);
        await WaitingForStartAsync(logger);

        pass.Calls.Should().BeEmpty("a hosted service is still starting");

        blocker.Release();
        await start.WaitAsync(Patience);
        await pass.FirstCall.WaitAsync(Patience);
        await host.StopAsync();

        pass.Calls.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StartedAsync_ClockAdvancedOverTwoSweepIntervals_SweepsTwiceAndOnlyWhenAdvanced()
    {
        var interval = TimeSpan.FromMinutes(30);
        var maxStagger = TimeSpan.FromMinutes(10);
        var stagger = RetentionStagger.OffsetFor(BsftRetentionOptions.NameFor<RecordingRetentionPass>(), maxStagger);
        var clock = new TimerSignallingTimeProvider(Now);
        var pass = new RecordingRetentionPass();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddScoped(_ => pass);
                services.AddBsftRetention<RecordingRetentionPass>(o =>
                {
                    o.RetentionPeriod = Retention;
                    o.SweepInterval = interval;
                    o.MaxStartupStagger = maxStagger;
                });
            })
            .Build();

        await host.StartAsync();
        (await clock.NextTimerAsync()).Should().Be(stagger);
        pass.Calls.Should().BeEmpty("the stagger has not elapsed on the injected clock");

        clock.Advance(stagger);
        (await clock.NextTimerAsync()).Should().Be(interval);
        pass.Calls.Should().ContainSingle("the first sweep follows the stagger");

        clock.Advance(interval - TimeSpan.FromTicks(1));
        pass.Calls.Should().ContainSingle("the interval has not passed on the injected clock");

        clock.Advance(TimeSpan.FromTicks(1));
        (await clock.NextTimerAsync()).Should().Be(interval);
        pass.Calls.Should().HaveCount(2, "the second interval passed on the injected clock");

        clock.Advance(interval);
        (await clock.NextTimerAsync()).Should().Be(interval);
        pass.Calls.Should().HaveCount(3);

        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_HostNeverStarted_NeverSweeps()
    {
        var pass = new RecordingRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<RecordingRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger);

        await sweeper.StartAsync(CancellationToken.None);
        await WaitingForStartAsync(logger);
        await sweeper.StopAsync(CancellationToken.None);

        sweeper.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        pass.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_BeforeTheHostStarted_EndsWithoutASweepOrAnError()
    {
        var pass = new RecordingRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<RecordingRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger);

        await sweeper.StartAsync(CancellationToken.None);
        await WaitingForStartAsync(logger);
        await sweeper.StoppingAsync(CancellationToken.None);
        await sweeper.StopAsync(CancellationToken.None);
        await sweeper.StoppedAsync(CancellationToken.None);

        sweeper.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        pass.Calls.Should().BeEmpty();
        logger.Levels.Should().NotContain(level => level >= LogLevel.Warning);
        logger.Messages.Should().Contain(message => message.Contains("stopped before it had started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartedAsync_PositiveStagger_IsMeasuredFromTheStartedSignal()
    {
        var maxStagger = TimeSpan.FromMinutes(10);
        var stagger = RetentionStagger.OffsetFor(BsftRetentionOptions.NameFor<RecordingRetentionPass>(), maxStagger);
        var clock = new TimerSignallingTimeProvider(Now);
        var pass = new RecordingRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<RecordingRetentionPass>>();
        using var sweeper = Build(pass, clock, logger, o => o.MaxStartupStagger = maxStagger);
        stagger.Should().BeGreaterThan(TimeSpan.FromMilliseconds(1), "the test splits the stagger in two");

        await sweeper.StartingAsync(CancellationToken.None);
        await sweeper.StartAsync(CancellationToken.None);
        await WaitingForStartAsync(logger);

        // Time that passes while the host is still starting does not count towards the stagger.
        clock.Advance(stagger);
        pass.Calls.Should().BeEmpty();

        await sweeper.StartedAsync(CancellationToken.None);
        (await clock.NextTimerAsync()).Should().Be(stagger);

        clock.Advance(stagger - TimeSpan.FromMilliseconds(1));
        pass.Calls.Should().BeEmpty("the stagger has not elapsed since the host started");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await pass.FirstCall.WaitAsync(Patience);
        await sweeper.StopAsync(CancellationToken.None);

        pass.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task StopAsync_WhileABatchIsInFlight_WaitsForItWithoutCancellingIt()
    {
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        var batchToken = await pass.NextBatchAsync();

        await sweeper.StoppingAsync(CancellationToken.None);
        var stop = sweeper.StopAsync(CancellationToken.None);

        stop.IsCompleted.Should().BeFalse("the batch in flight has not finished");
        batchToken.IsCancellationRequested.Should().BeFalse("a stop drains the batch in flight");

        // A full batch: without the stop, another one would follow at once.
        pass.Release(10);
        await stop.WaitAsync(Patience);

        batchToken.IsCancellationRequested.Should().BeFalse();
        pass.Calls.Should().Be(1, "no further batch starts once a stop was requested");
        sweeper.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        logger.Levels.Should().NotContain(level => level >= LogLevel.Warning);
    }

    [Fact]
    public async Task StoppingAsync_BetweenBatches_StartsNoFurtherBatch()
    {
        var clock = new TimerSignallingTimeProvider(Now);
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(pass, clock, logger, o =>
        {
            o.BatchSize = 10;
            o.BatchDelay = TimeSpan.FromSeconds(5);
        });
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();
        pass.Release(10);

        // The pause before the second batch has begun.
        (await clock.NextTimerAsync()).Should().Be(TimeSpan.FromSeconds(5));
        await sweeper.StoppingAsync(CancellationToken.None);

        // The stop request alone ends the sweeper: the pause ends early and no batch follows it.
        await sweeper.ExecuteTask!.WaitAsync(Patience);
        clock.Advance(TimeSpan.FromSeconds(5));
        await sweeper.StopAsync(CancellationToken.None);

        pass.Calls.Should().Be(1);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Warning);
    }

    [Fact]
    public async Task StopAsync_BatchOutlastsTheShutdownToken_CancelsTheBatchWithoutAnError()
    {
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        var batchToken = await pass.NextBatchAsync();
        using var shutdown = new CancellationTokenSource();

        await sweeper.StoppingAsync(CancellationToken.None);
        var stop = sweeper.StopAsync(shutdown.Token);
        batchToken.IsCancellationRequested.Should().BeFalse("the shutdown timeout has not elapsed yet");

        await shutdown.CancelAsync();
        await stop.WaitAsync(Patience);
        batchToken.IsCancellationRequested.Should().BeTrue("the shutdown timeout elapsed, and the state is set before the stop returns");
        await sweeper.ExecuteTask!.WaitAsync(Patience);

        sweeper.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue();
        pass.Calls.Should().Be(1);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Error, "cancellation on shutdown is not a failure");
    }

    [Fact]
    public async Task StopAsync_BatchIgnoresItsToken_CompletesWhenTheShutdownTokenFires()
    {
        var pass = new GatedRetentionPass { IgnoresCancellation = true };
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();
        using var shutdown = new CancellationTokenSource();

        var stop = sweeper.StopAsync(shutdown.Token);
        await shutdown.CancelAsync();

        // The stop never outlasts the host's shutdown timeout, whatever the pass does with its token.
        await stop.WaitAsync(Patience);
        sweeper.ExecuteTask!.IsCompleted.Should().BeFalse();

        pass.Release(10);
        await sweeper.ExecuteTask.WaitAsync(Patience);

        pass.Calls.Should().Be(1);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Error);
    }

    [Fact]
    public async Task Dispose_WhileABatchIsInFlight_CancelsTheBatch()
    {
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        var batchToken = await pass.NextBatchAsync();

        sweeper.Dispose();
        await sweeper.ExecuteTask!.WaitAsync(Patience);

        batchToken.IsCancellationRequested.Should().BeTrue();
        pass.Calls.Should().Be(1);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Error);

        var again = () => sweeper.Dispose();
        again.Should().NotThrow("disposing twice is harmless");
    }

    [Fact]
    public async Task StopAsync_BatchTokenCallbackBlocks_ReturnsOnceTheShutdownTokenFires()
    {
        // A store's command cancellation can block (Npgsql opens a connection for the cancel request);
        // the stop must not wait for it past the host's shutdown timeout.
        using var callbackEntered = new ManualResetEventSlim();
        using var callbackGate = new ManualResetEventSlim();
        var pass = new GatedRetentionPass
        {
            OnCancel = () =>
            {
                callbackEntered.Set();
                callbackGate.Wait();
            },
        };
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        var batchToken = await pass.NextBatchAsync();
        using var shutdown = new CancellationTokenSource();

        var stop = sweeper.StopAsync(shutdown.Token);
        await shutdown.CancelAsync();
        await stop.WaitAsync(Patience);

        callbackEntered.Wait(Patience).Should().BeTrue("the batch's cancellation callback ran");
        stop.IsCompletedSuccessfully.Should().BeTrue();
        callbackGate.IsSet.Should().BeFalse("the callback is still blocked, and the stop returned regardless");
        batchToken.IsCancellationRequested.Should().BeTrue("the batch was told to cancel");

        callbackGate.Set();
        await sweeper.ExecuteTask!.WaitAsync(Patience);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Error);
    }

    [Fact]
    public async Task StopAsync_BatchTokenCallbackThrows_IsObservedAndLoggedBelowError()
    {
        var pass = new GatedRetentionPass { OnCancel = () => throw new InvalidOperationException("cancel request failed") };
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();

        await sweeper.StopAsync(new CancellationToken(canceled: true));

        await logger.WrittenAsync(entry => entry.Message.Contains("failed in a callback", StringComparison.Ordinal))
            .WaitAsync(Patience);
        await sweeper.ExecuteTask!.WaitAsync(Patience);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Error);
    }

    [Fact]
    public async Task StopAsync_BatchFailsWithAStoreExceptionAfterTheShutdownToken_LogsNoError()
    {
        // SqlClient reports a cancelled command as a SqlException rather than a cancellation.
        var failure = new InvalidOperationException("Operation cancelled by user.");
        var pass = new GatedRetentionPass { FailureOnCancel = failure };
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();
        using var shutdown = new CancellationTokenSource();

        await sweeper.StoppingAsync(CancellationToken.None);
        var stop = sweeper.StopAsync(shutdown.Token);
        await shutdown.CancelAsync();
        await stop.WaitAsync(Patience);
        await sweeper.ExecuteTask!.WaitAsync(Patience);

        logger.Levels.Should().NotContain(level => level >= LogLevel.Error, "the failure is the shutdown cancellation");
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Debug && entry.Exception == failure);
    }

    [Fact]
    public async Task StartedAsync_BatchFailsWithAStoreExceptionWhileNotShuttingDown_LogsAnError()
    {
        var failure = new InvalidOperationException("Operation cancelled by user.");
        var pass = new RecordingRetentionPass { Failure = failure };
        var logger = new RecordingLogger<RetentionSweeper<RecordingRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);

        await logger.WrittenAsync(entry => entry.Level == LogLevel.Error && entry.Exception == failure).WaitAsync(Patience);
        await sweeper.StopAsync(CancellationToken.None);

        sweeper.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_AfterDisposeWithoutAStop_ThrowsNothing()
    {
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();

        sweeper.Dispose();
        var stopping = () => sweeper.StoppingAsync(CancellationToken.None);
        var stop = () => sweeper.StopAsync(new CancellationToken(canceled: true));

        await stopping.Should().NotThrowAsync();
        await stop.Should().NotThrowAsync();
        await sweeper.ExecuteTask!.WaitAsync(Patience);
    }

    [Fact]
    public async Task StopAsync_ShutdownTokenAlreadyFired_TheBatchTokenReadsCancelledWhenItReturns()
    {
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        var batchToken = await pass.NextBatchAsync();

        await sweeper.StopAsync(new CancellationToken(canceled: true));

        batchToken.IsCancellationRequested.Should().BeTrue("the cancelled state is set synchronously, not by a queued task");
        await sweeper.ExecuteTask!.WaitAsync(Patience);
    }

    [Fact]
    public async Task Dispose_WithoutAStop_TheBatchTokenReadsCancelledWhenItReturns()
    {
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        var batchToken = await pass.NextBatchAsync();

        sweeper.Dispose();

        batchToken.IsCancellationRequested.Should().BeTrue("the cancelled state is set synchronously, not by a queued task");
        await sweeper.ExecuteTask!.WaitAsync(Patience);
    }

    [Fact]
    public async Task Dispose_BatchReturnsFullAfterwards_StartsNoFurtherBatch()
    {
        var pass = new GatedRetentionPass { IgnoresCancellation = true };
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();

        sweeper.Dispose();

        // A full batch: without the dispose, the next one would start at once.
        pass.Release(10);
        await sweeper.ExecuteTask!.WaitAsync(Patience);

        pass.Calls.Should().Be(1, "no batch starts once the sweeper has been disposed");
        logger.Levels.Should().NotContain(level => level >= LogLevel.Error);
    }

    [Fact]
    public async Task Dispose_BatchTokenCallbackBlocks_ReturnsWhileItIsStillBlocked()
    {
        using var callbackEntered = new ManualResetEventSlim();
        using var callbackGate = new ManualResetEventSlim();
        var pass = new GatedRetentionPass
        {
            OnCancel = () =>
            {
                callbackEntered.Set();
                callbackGate.Wait();
            },
        };
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();

        sweeper.Dispose();

        callbackEntered.Wait(Patience).Should().BeTrue("the batch's cancellation callback ran");
        callbackGate.IsSet.Should().BeFalse("the callback is still blocked, and the dispose returned regardless");

        callbackGate.Set();
        await sweeper.ExecuteTask!.WaitAsync(Patience);
    }

    [Fact]
    public async Task Dispose_TheBatchTokensSource_IsNotDisposed()
    {
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        var batchToken = await pass.NextBatchAsync();

        sweeper.Dispose();
        await sweeper.ExecuteTask!.WaitAsync(Patience);

        // Read by reflection rather than through WaitHandle, which would allocate the very wait handle
        // the sweeper's sources must never get.
        batchToken.IsCancellationRequested.Should().BeTrue();
        foreach (var name in new[] { "_stopRequested", "_shutdownElapsed" })
        {
            var source = OwnField(typeof(RetentionSweeper<GatedRetentionPass>), name).GetValue(sweeper);

            OwnField(typeof(CancellationTokenSource), "_disposed").GetValue(source)
                .Should().Be(false, $"the sweeper never disposes {name}");
        }
    }

    [Fact]
    public async Task Dispose_AfterAFullLifecycle_OwnSourcesHaveNeitherATimerNorAWaitHandle()
    {
        // Pins the reason the sources are never disposed: without a timer and without a wait handle a
        // source holds nothing to release. The fields are the runtime's own, read by reflection because
        // nothing public tells; a runtime that renames them fails this test rather than passing it.
        var pass = new GatedRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartingAsync(CancellationToken.None);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();
        await sweeper.StoppingAsync(CancellationToken.None);
        await sweeper.StopAsync(new CancellationToken(canceled: true));
        await sweeper.StoppedAsync(CancellationToken.None);
        sweeper.Dispose();
        await sweeper.ExecuteTask!.WaitAsync(Patience);

        foreach (var name in new[] { "_stopRequested", "_shutdownElapsed" })
        {
            var source = (CancellationTokenSource?)OwnField(typeof(RetentionSweeper<GatedRetentionPass>), name).GetValue(sweeper);

            source.Should().NotBeNull();
            source!.IsCancellationRequested.Should().BeTrue();
            OwnField(typeof(CancellationTokenSource), "_timer").GetValue(source).Should().BeNull($"{name} must never get a timer");
            OwnField(typeof(CancellationTokenSource), "_kernelEvent").GetValue(source).Should().BeNull($"{name} must never get a wait handle");
        }
    }

    [Fact]
    public async Task Dispose_PassThrowsOnItsCancelledTokenAtOnce_LogsNoError()
    {
        // The loop's drain learns of the dispose on the thread pool; a pass that throws on its token
        // before then must not be reported as a cancellation without a stop.
        var pass = new GatedRetentionPass { IgnoresCancellation = true, ChecksTokenOnRelease = true };
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(pass, new TimerSignallingTimeProvider(Now), logger, o => o.BatchSize = 10);
        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StartedAsync(CancellationToken.None);
        await pass.NextBatchAsync();

        sweeper.Dispose();
        pass.Release(10);
        await sweeper.ExecuteTask!.WaitAsync(Patience);

        pass.Calls.Should().Be(1);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Error);
    }

    [Fact]
    public void ReportPassCanceledWithoutStop_AfterDispose_LogsAtDebug()
    {
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        var sweeper = Build(new GatedRetentionPass(), new TimerSignallingTimeProvider(Now), logger);

        sweeper.Dispose();
        ((IPollReporter)sweeper).ReportPassCanceledWithoutStop();

        logger.Levels.Should().ContainSingle().Which.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public void ReportPassCanceledWithoutStop_WhileRunning_LogsAnError()
    {
        var logger = new RecordingLogger<RetentionSweeper<GatedRetentionPass>>();
        using var sweeper = Build(new GatedRetentionPass(), new TimerSignallingTimeProvider(Now), logger);

        ((IPollReporter)sweeper).ReportPassCanceledWithoutStop();

        logger.Levels.Should().ContainSingle().Which.Should().Be(LogLevel.Error);
    }

    [Fact]
    public void Source_RetentionPackage_NeverReadsAWaitHandleOrSetsACancelAfter()
    {
        // Guards the rule on the sweeper's never-disposed sources: a wait handle or a timer would give
        // them something to dispose. Comments are skipped, since the rule itself names both.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Bsft.sln")))
            root = root.Parent;
        root.Should().NotBeNull("the test runs inside the repository");

        var offending = Directory.EnumerateFiles(Path.Combine(root!.FullName, "src", "Bsft.Retention"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, index, code: CodeOf(line))))
            .Where(line => line.code.Contains(".WaitHandle", StringComparison.Ordinal) || line.code.Contains("CancelAfter(", StringComparison.Ordinal))
            .Select(line => $"{Path.GetFileName(line.file)}:{line.index + 1}")
            .ToList();

        offending.Should().BeEmpty();

        static string CodeOf(string line)
        {
            var comment = line.IndexOf("//", StringComparison.Ordinal);

            return comment < 0 ? line : line[..comment];
        }
    }

    private static System.Reflection.FieldInfo OwnField(Type type, string name) =>
        type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{type.Name} has no field {name}.");
}
