using Bsft.Outbox.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Outbox.Tests;

/// <summary>Tests the loop around the drain pass: shutdown grace, wake signal and interval.</summary>
public sealed class OutboxDrainWorkerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(7);

    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<OutboxDrainWorker<string>> _logger = new();
    private readonly CancellationTokenSource _stop = new();

    private OutboxDrainWorker<string> Worker(
        IOutboxDrainer<string> drainer,
        IOutboxWakeSignal? signal = null,
        TimeSpan? grace = null) =>
        new(
            drainer,
            Options.Create(new BsftOutboxOptions { PollInterval = Interval }),
            Options.Create(new BsftOutboxDrainWorkerOptions { ShutdownGrace = grace ?? TimeSpan.FromSeconds(30) }),
            signal,
            _clock,
            _logger);

    [Fact]
    public async Task RunLoopAsync_WhenTheIntervalElapses_DrainsAgain()
    {
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer).RunLoopAsync(_stop.Token);

        await drainer.StartedAsync(1);
        await _clock.WaitForTimerAsync(Interval);

        drainer.Passes.Should().Be(1);

        _clock.Advance(Interval);
        await drainer.StartedAsync(2);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenTheIntervalHasNotElapsed_DoesNotDrainAgain()
    {
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);
        _clock.Advance(Interval - TimeSpan.FromTicks(1));

        drainer.Passes.Should().Be(1);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenAPassAppliedRows_DrainsUntilAPassAppliesNothing()
    {
        var applied = new Queue<int>([3, 2, 0]);
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(applied.Dequeue()));
        var running = Worker(drainer).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);

        drainer.Passes.Should().Be(3);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenTheSignalIsRaised_DrainsBeforeTheIntervalElapses()
    {
        var signal = new ManualWakeSignal();
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer, signal).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);
        signal.Raise();
        await drainer.StartedAsync(2);

        _clock.GetUtcNow().Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenTheSignalWasRaisedBeforeTheWait_DrainsAtOnce()
    {
        var signal = new ManualWakeSignal();
        signal.Raise();
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer, signal).RunLoopAsync(_stop.Token);

        await drainer.StartedAsync(2);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenTheSignalNeverComes_FallsBackToTheInterval()
    {
        var signal = new ManualWakeSignal();
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer, signal).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);
        _clock.Advance(Interval);
        await drainer.StartedAsync(2);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenAPassFails_ReportsItAndTriesAgainAfterTheInterval()
    {
        var drainer = new ScriptedDrainer((pass, _) => pass == 1
            ? Task.FromException<int>(new InvalidOperationException("store down"))
            : Task.FromResult(0));
        var running = Worker(drainer).RunLoopAsync(_stop.Token);

        await _logger.WrittenAsync(entry => entry.Level == LogLevel.Error);
        await _clock.WaitForTimerAsync(Interval);
        _clock.Advance(Interval);
        await drainer.StartedAsync(2);

        _logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Exception.Should().BeOfType<InvalidOperationException>();

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenAPassIsCanceledWithoutAStop_ReportsItAndKeepsRunning()
    {
        var drainer = new ScriptedDrainer((pass, _) => pass == 1
            ? Task.FromCanceled<int>(new CancellationToken(true))
            : Task.FromResult(0));
        var running = Worker(drainer).RunLoopAsync(_stop.Token);

        await _logger.WrittenAsync(entry => entry.Level == LogLevel.Error);
        await _clock.WaitForTimerAsync(Interval);
        _clock.Advance(Interval);
        await drainer.StartedAsync(2);

        _logger.Messages.Should().ContainSingle().Which.Should().Contain("not asked to stop");

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenStopped_LogsAtDebugAndEnds()
    {
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);
        await StopAsync(running);

        _logger.Levels.Should().Equal(LogLevel.Debug);
    }

    [Fact]
    public async Task RunLoopAsync_WhenStoppedWhileWaitingForTheSignal_Ends()
    {
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer, new ManualWakeSignal()).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);
        await StopAsync(running);

        drainer.Passes.Should().Be(1);
    }

    [Fact]
    public async Task RunLoopAsync_WhenStoppedDuringAPass_DoesNotStartAnotherPassOfTheBacklog()
    {
        var release = new TaskCompletionSource();
        var drainer = new ScriptedDrainer(async (_, _) =>
        {
            await release.Task;

            return 5;
        });
        var running = Worker(drainer).RunLoopAsync(_stop.Token);

        await drainer.StartedAsync(1);
        await _stop.CancelAsync();
        release.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        drainer.Passes.Should().Be(1);
    }

    /// <summary>The pass token outlives the stop: a row in flight is not cancelled by the shutdown itself.</summary>
    [Fact]
    public async Task RunLoopAsync_WhenStopped_LetsARowInFlightFinishItsEffectAndItsProcessedMark()
    {
        var effect = new GatedSideEffect();
        var store = new RecordingOutboxStore();
        store.Pending.Add(new LeasedOutboxRow<string>(7, "mail", 0, Guid.NewGuid()));
        var running = Worker(Drainer(store, effect), grace: TimeSpan.FromSeconds(30)).RunLoopAsync(_stop.Token);

        await effect.Started;
        await _stop.CancelAsync();

        effect.TokenWasCancelled.Should().BeFalse();

        effect.Release();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        effect.TokenWasCancelled.Should().BeFalse();
        store.Processed.Should().Equal(7);
    }

    /// <summary>The grace is a bound, not a promise: a row that outlasts it is cancelled and stays pending.</summary>
    [Fact]
    public async Task RunLoopAsync_WhenTheGraceElapsesBeforeTheEffectEnds_CancelsThePassAndLeavesTheRowPending()
    {
        var effect = new GatedSideEffect();
        var store = new RecordingOutboxStore();
        store.Pending.Add(new LeasedOutboxRow<string>(7, "mail", 0, Guid.NewGuid()));
        var running = Worker(Drainer(store, effect), grace: TimeSpan.FromMilliseconds(50)).RunLoopAsync(_stop.Token);

        await effect.Started;
        await _stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        effect.TokenWasCancelled.Should().BeTrue();
        store.Processed.Should().BeEmpty();
        store.Abandoned.Should().BeEmpty();
    }

    /// <summary>The grace belongs to the row in flight: after the stop no further row of the leased batch is started.</summary>
    [Fact]
    public async Task RunLoopAsync_WhenStoppedDuringARow_StartsNoFurtherRowOfTheBatch()
    {
        var effect = new GatedSideEffect();
        var store = new RecordingOutboxStore();
        store.Pending.Add(new LeasedOutboxRow<string>(1, "first", 0, Guid.NewGuid()));
        store.Pending.Add(new LeasedOutboxRow<string>(2, "second", 0, Guid.NewGuid()));
        var running = Worker(Drainer(store, effect), grace: TimeSpan.FromSeconds(30)).RunLoopAsync(_stop.Token);

        await effect.Started;
        await _stop.CancelAsync();
        effect.Release();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        effect.Applied.Should().Equal("first");
        store.Processed.Should().Equal(1);
    }

    [Fact]
    public async Task RunLoopAsync_WhenTheGraceIsZero_CancelsThePassWithTheStop()
    {
        var effect = new GatedSideEffect();
        var store = new RecordingOutboxStore();
        store.Pending.Add(new LeasedOutboxRow<string>(7, "mail", 0, Guid.NewGuid()));
        var running = Worker(Drainer(store, effect), grace: TimeSpan.Zero).RunLoopAsync(_stop.Token);

        await effect.Started;
        await _stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        effect.TokenWasCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheHostStarts_RunsTheLoop()
    {
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        using var worker = Worker(drainer);

        await worker.StartAsync(_stop.Token);
        await drainer.StartedAsync(1);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Constructor_WhenARequiredArgumentIsNull_Throws()
    {
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var outbox = Options.Create(new BsftOutboxOptions());
        var worker = Options.Create(new BsftOutboxDrainWorkerOptions());

        var cases = new (string Parameter, Action Build)[]
        {
            ("drainer", () => new OutboxDrainWorker<string>(null!, outbox, worker, null, _clock, NullLogger.Instance)),
            ("outboxOptions", () => new OutboxDrainWorker<string>(drainer, null!, worker, null, _clock, NullLogger.Instance)),
            ("workerOptions", () => new OutboxDrainWorker<string>(drainer, outbox, null!, null, _clock, NullLogger.Instance)),
            ("timeProvider", () => new OutboxDrainWorker<string>(drainer, outbox, worker, null, null!, NullLogger.Instance)),
            ("logger", () => new OutboxDrainWorker<string>(drainer, outbox, worker, null, _clock, null!)),
        };

        foreach (var (parameter, build) in cases)
            build.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }

    private async Task StopAsync(Task running)
    {
        await _stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private OutboxDrainer<string> Drainer(RecordingOutboxStore store, GatedSideEffect effect) =>
        new(store, effect, new OutboxRetrySchedule(new Random(1)), new BsftOutboxOptions { PollInterval = Interval }, _clock, NullLogger.Instance);

    /// <summary>A side effect that runs until the test releases it or its token is cancelled.</summary>
    private sealed class GatedSideEffect : IOutboxSideEffect<string>
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        public bool TokenWasCancelled { get; private set; }

        public List<string> Applied { get; } = [];

        public void Release() => _released.TrySetResult();

        public async Task ApplyAsync(string payload, CancellationToken ct = default)
        {
            Applied.Add(payload);
            _started.TrySetResult();

            try
            {
                await _released.Task.WaitAsync(ct);
            }
            finally
            {
                TokenWasCancelled = ct.IsCancellationRequested;
            }
        }

        public string Describe(string payload) => payload;
    }
}
