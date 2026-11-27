using Bsft.Hosting.BackgroundWork.Tests.Fakes;
using Bsft.Hosting.StartGates.DependencyInjection;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bsft.Hosting.BackgroundWork.Tests;

public sealed class PollingBackgroundServiceBaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly ManualTimeProvider _clock = new(Now);
    private readonly RecordingLogger<ScriptedPollingService> _logger = new();

    private ScriptedPollingService Build(bool enabled = true, Exception? startingFailure = null) =>
        new(_logger, _clock, Interval, enabled, startingFailure);

    [Fact]
    public void Constructor_WhenTheLoggerIsNull_Throws()
    {
        var act = () => new ScriptedPollingService(null!, _clock, Interval);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_EndsAtOnceAndLogsItOnce()
    {
        var service = Build(enabled: false);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(Patience);

        service.Ticks.Should().Be(0);
        service.Startings.Should().Be(0);
        _logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("ScriptedPollingService is disabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabledAndTheLoggerIsOff_EndsWithoutLogging()
    {
        var service = new ScriptedPollingService(NullLogger.Instance, _clock, Interval, enabled: false);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(Patience);

        service.Ticks.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithTheDefaults_IsEnabledAndHasNoStartWork()
    {
        var service = new MinimalPollingService(_clock);

        await service.StartAsync(CancellationToken.None);
        await service.FirstTick.WaitAsync(Patience);
        await service.StopAsync(CancellationToken.None);

        service.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_RunsTheStartWorkOnceBeforeTheFirstPass()
    {
        var service = Build().Then(true).Then(true);

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await service.NextTickAsync();
        await service.NextTickAsync();

        service.Startings.Should().Be(1);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheStartWorkFails_FaultsTheServiceWithoutRunningAPass()
    {
        var service = Build(startingFailure: new InvalidOperationException("no schema"));


        // Where the failure surfaces differs by runtime: StartAsync throws it on .NET 8, and on .NET 10 the
        // start returns and the failure is on the execute task. Either is the service faulting.
        var act = async () =>
        {
            await service.StartAsync(CancellationToken.None);
            await service.ExecuteTask!.WaitAsync(Patience);
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("no schema");
        service.Ticks.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheIntervalIsZero_FaultsTheServiceNamingItBeforeTheStartWork() =>
        await AssertInvalidIntervalAsync(TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WhenTheIntervalIsNegative_FaultsTheServiceNamingItBeforeTheStartWork() =>
        await AssertInvalidIntervalAsync(TimeSpan.FromSeconds(-5));

    [Fact]
    public async Task ExecuteAsync_WhenTheIntervalIsInfinite_FaultsTheServiceNamingItBeforeTheStartWork() =>
        await AssertInvalidIntervalAsync(Timeout.InfiniteTimeSpan);

    [Fact]
    public async Task StartAsync_WhenTheFirstPassBlocksWithoutAwaiting_ReturnsBeforeThePassEnds()
    {
        using var release = new ManualResetEventSlim();
        var released = false;
        var service = Build().Then(_ =>
        {
            released = release.Wait(Patience);

            return Task.FromResult(false);
        });

        await service.StartAsync(CancellationToken.None);
        release.Set();
        await service.NextTickAsync();
        await _clock.WaitForTimerAsync(Interval);

        released.Should().BeTrue("the host's start must not wait for the pass");
        await service.StopAsync(CancellationToken.None);
    }

    private async Task AssertInvalidIntervalAsync(TimeSpan interval)
    {
        var service = new ScriptedPollingService(_logger, _clock, interval);

        // Where the failure surfaces differs by runtime, as for a failing start work.
        var act = async () =>
        {
            await service.StartAsync(CancellationToken.None);
            await service.ExecuteTask!.WaitAsync(Patience);
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("ScriptedPollingService has a poll interval of*greater than zero*");
        service.Startings.Should().Be(0);
        service.Ticks.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPassLeavesWork_StartsTheNextPassWithoutWaiting()
    {
        var service = Build().Then(true).Then(true);

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await service.NextTickAsync();
        await service.NextTickAsync();

        _clock.PendingTimerCount.Should().Be(0);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPassIsIdle_WaitsThePollIntervalBeforeTheNextPass()
    {
        var service = Build().Then(false);

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await _clock.WaitForTimerAsync(Interval);

        _clock.Advance(Interval - TimeSpan.FromSeconds(1));
        service.Ticks.Should().Be(1);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await service.NextTickAsync();

        service.Ticks.Should().Be(2);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPassFails_LogsItWaitsTheIntervalAndGoesOn()
    {
        var failure = new InvalidOperationException("the store is down");
        var service = Build().Then(_ => Task.FromException<bool>(failure));

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await _clock.WaitForTimerAsync(Interval);

        service.Ticks.Should().Be(1);
        _logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error
            && ReferenceEquals(entry.Exception, failure)
            && entry.Message.Contains("ScriptedPollingService failed", StringComparison.Ordinal));
        _clock.Advance(Interval);
        await service.NextTickAsync();
        service.ExecuteTask!.IsCompleted.Should().BeFalse();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPassIsCanceledWithoutAStop_LogsItWaitsTheIntervalAndGoesOn()
    {
        var service = Build().Then(_ => Task.FromCanceled<bool>(new CancellationToken(true)));

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await _clock.WaitForTimerAsync(Interval);

        _logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("not asked to stop", StringComparison.Ordinal));
        _clock.Advance(Interval);
        await service.NextTickAsync();
        service.ExecuteTask!.IsCompleted.Should().BeFalse();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheLoggerIsOff_SurvivesFailuresAndStopsCleanly()
    {
        var service = new ScriptedPollingService(NullLogger.Instance, _clock, Interval)
            .Then(_ => Task.FromException<bool>(new InvalidOperationException()))
            .Then(_ => Task.FromCanceled<bool>(new CancellationToken(true)));

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await _clock.WaitForTimerAsync(Interval);
        _clock.Advance(Interval);
        await service.NextTickAsync();
        await _clock.WaitForTimerAsync(Interval);
        _clock.Advance(Interval);
        await service.NextTickAsync();
        await service.StopAsync(CancellationToken.None);

        service.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    public async Task StopAsync_DuringAPass_EndsCleanlyWithoutAnErrorInTheLog()
    {
        var service = Build();

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await service.StopAsync(CancellationToken.None);

        service.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion);
        _logger.Levels.Should().NotContain(level => level >= LogLevel.Warning);
        _logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("is stopping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopAsync_DuringTheWait_EndsCleanlyWithoutAnErrorInTheLog()
    {
        var service = Build().Then(false);

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await _clock.WaitForTimerAsync(Interval);
        await service.StopAsync(CancellationToken.None);

        service.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion);
        _logger.Levels.Should().NotContain(level => level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutATimeProvider_WaitsOnTheSystemClock()
    {
        var service = new ScriptedPollingService(_logger, null, TimeSpan.FromMilliseconds(1))
            .Then(false);

        await service.StartAsync(CancellationToken.None);
        await service.NextTickAsync();
        await service.NextTickAsync();

        service.Ticks.Should().Be(2);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GatedRegistration_WaitsForTheStartGateBeforeStartingTheService()
    {
        var gate = new ControllableStartGate();
        var service = Build().Then(false);
        using var provider = new ServiceCollection()
            .AddSingleton(service)
            .AddBsftGatedHostedService<ScriptedPollingService>()
            .AddBsftHostedWorkStartGate(_ => gate)
            .BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        await gate.Awaited.WaitAsync(Patience);

        service.Startings.Should().Be(0);
        service.Ticks.Should().Be(0);

        gate.Open();
        await service.NextTickAsync();

        service.Startings.Should().Be(1);
        service.Ticks.Should().Be(1);
        await hosted.StopAsync(CancellationToken.None);
    }
}
