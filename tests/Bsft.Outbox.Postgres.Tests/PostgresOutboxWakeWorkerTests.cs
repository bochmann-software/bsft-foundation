using Bsft.Outbox.Postgres.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Outbox.Postgres.Tests;

/// <summary>Tests the worker that keeps the listening session alive and the signal it raises.</summary>
public sealed class PostgresOutboxWakeWorkerTests
{
    private static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(3);

    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<PostgresOutboxWakeWorker> _logger = new();
    private readonly PostgresOutboxWakeSignal _signal = new();
    private readonly CancellationTokenSource _stop = new();

    private PostgresOutboxWakeWorker Worker(
        IPostgresListenSession session) =>
        new(
            session,
            Options.Create(new BsftOutboxPostgresOptions
            {
                NotificationChannel = "orders_outbox",
                ConnectionString = "Host=orders",
                ReconnectDelay = Reconnect,
            }),
            _signal,
            new PostgresListenLoop(_clock),
            _logger);

    [Fact]
    public async Task RunLoopAsync_WhenASessionStarts_ListensOnTheConfiguredChannelAndConnection()
    {
        var session = new ScriptedListenSession((_, _, token) => Task.Delay(Timeout.Infinite, token));
        var running = Worker(session).RunLoopAsync(_stop.Token);

        await session.FirstSessionStarted;

        session.Last.Should().Be(("Host=orders", "orders_outbox"));

        await StopAsync(running);
    }

    [Fact]
    public async Task WaitAsync_WhenTheSessionCalledBack_CompletesWithoutWaitingForAWrite()
    {
        // The callback after the subscription covers rows written while nothing was listening.
        var session = new ScriptedListenSession(async (_, onNotified, token) =>
        {
            await onNotified(token);
            await Task.Delay(Timeout.Infinite, token);
        });
        var worker = Worker(session);
        var running = worker.RunLoopAsync(_stop.Token);

        await _signal.WaitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        await StopAsync(running);
    }

    [Fact]
    public async Task WaitAsync_WhenNothingWasAnnounced_DoesNotComplete()
    {
        var wait = _signal.WaitAsync();
        await Task.Delay(50);

        wait.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task WaitAsync_WhenAbandoned_KeepsTheSignalThatArrivesLater()
    {
        var notify = default(Func<CancellationToken, Task>);
        var session = new ScriptedListenSession((_, onNotified, token) =>
        {
            notify = onNotified;

            return Task.Delay(Timeout.Infinite, token);
        });
        var worker = Worker(session);
        var running = worker.RunLoopAsync(_stop.Token);
        await session.FirstSessionStarted;

        using var abandon = new CancellationTokenSource();
        var abandoned = _signal.WaitAsync(abandon.Token);
        await abandon.CancelAsync();
        var act = async () => await abandoned;
        await act.Should().ThrowAsync<OperationCanceledException>();

        await notify!(_stop.Token);

        await _signal.WaitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        await StopAsync(running);
    }

    [Fact]
    public async Task WaitAsync_WhenSeveralNotificationsArrivedFirst_CompletesOnceForAllOfThem()
    {
        var session = new ScriptedListenSession(async (_, onNotified, token) =>
        {
            await onNotified(token);
            await onNotified(token);
            await onNotified(token);
            await Task.Delay(Timeout.Infinite, token);
        });
        var worker = Worker(session);
        var running = worker.RunLoopAsync(_stop.Token);
        await session.FirstSessionStarted;

        await _signal.WaitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var second = _signal.WaitAsync();
        await Task.Delay(50);

        second.IsCompleted.Should().BeFalse();

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenASessionFails_ReportsItAndReconnectsAfterTheDelay()
    {
        var session = new ScriptedListenSession((number, _, token) => number == 1
            ? Task.FromException(new InvalidOperationException("connection reset"))
            : Task.Delay(Timeout.Infinite, token));
        var running = Worker(session).RunLoopAsync(_stop.Token);

        await _logger.WrittenAsync(entry => entry.Level == LogLevel.Error);
        await _clock.WaitForTimerAsync(Reconnect);

        session.Sessions.Should().Be(1);

        _clock.Advance(Reconnect);
        await WaitUntilAsync(() => session.Sessions == 2);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenASessionEndsCleanly_ReconnectsAfterTheDelay()
    {
        var session = new ScriptedListenSession((number, _, token) => number == 1
            ? Task.CompletedTask
            : Task.Delay(Timeout.Infinite, token));
        var running = Worker(session).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Reconnect);
        _clock.Advance(Reconnect);
        await WaitUntilAsync(() => session.Sessions == 2);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenASessionIsCanceledWithoutAStop_ReportsItAndReconnects()
    {
        var session = new ScriptedListenSession((number, _, token) => number == 1
            ? Task.FromCanceled(new CancellationToken(true))
            : Task.Delay(Timeout.Infinite, token));
        var running = Worker(session).RunLoopAsync(_stop.Token);

        await _logger.WrittenAsync(entry => entry.Level == LogLevel.Error);
        await _clock.WaitForTimerAsync(Reconnect);
        _clock.Advance(Reconnect);
        await WaitUntilAsync(() => session.Sessions == 2);

        _logger.Messages.Should().ContainSingle().Which.Should().Contain("not asked to stop");

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenStopped_LogsAtDebugAndEnds()
    {
        var session = new ScriptedListenSession((_, _, token) => Task.Delay(Timeout.Infinite, token));
        var running = Worker(session).RunLoopAsync(_stop.Token);

        await session.FirstSessionStarted;
        await StopAsync(running);

        _logger.Levels.Should().Equal(LogLevel.Debug);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheHostStarts_RunsTheLoop()
    {
        var session = new ScriptedListenSession((_, _, token) => Task.Delay(Timeout.Infinite, token));
        using var worker = Worker(session);

        await worker.StartAsync(_stop.Token);
        await session.FirstSessionStarted;
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Constructor_WhenARequiredArgumentIsNull_Throws()
    {
        var session = new ScriptedListenSession((_, _, token) => Task.CompletedTask);
        var options = Options.Create(new BsftOutboxPostgresOptions { NotificationChannel = "orders_outbox" });
        var loop = new PostgresListenLoop(_clock);

        var cases = new (string Parameter, Action Build)[]
        {
            ("session", () => new PostgresOutboxWakeWorker(null!, options, _signal, loop, NullLogger.Instance)),
            ("options", () => new PostgresOutboxWakeWorker(session, null!, _signal, loop, NullLogger.Instance)),
            ("signal", () => new PostgresOutboxWakeWorker(session, options, null!, loop, NullLogger.Instance)),
            ("loop", () => new PostgresOutboxWakeWorker(session, options, _signal, null!, NullLogger.Instance)),
            ("logger", () => new PostgresOutboxWakeWorker(session, options, _signal, loop, null!)),
        };

        foreach (var (parameter, build) in cases)
            build.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }

    [Fact]
    public void Constructor_WithAChannelOutsideTheGrammar_Throws()
    {
        var session = new ScriptedListenSession((_, _, token) => Task.CompletedTask);
        var options = Options.Create(new BsftOutboxPostgresOptions { NotificationChannel = "Bad Channel" });

        var act = () => new PostgresOutboxWakeWorker(session, options, _signal, new PostgresListenLoop(_clock), NullLogger.Instance);

        act.Should().Throw<ArgumentException>();
    }

    private async Task StopAsync(Task running)
    {
        await _stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5);

        condition().Should().BeTrue();
    }
}
